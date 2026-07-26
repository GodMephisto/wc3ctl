// src/Wc3.Studio/Panels/TerrainGlView.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using System.Threading.Tasks;
using Silk.NET.Core.Contexts;
using Silk.NET.OpenGL;
using Wc3.Render;

namespace Wc3.Studio.Panels;

/// <summary>A ray-pick hit on a placed widget: the widget's creation number plus which
/// file it identifies (unit and doodad creation-number spaces can overlap, so the kind
/// bit tells the workspace which instance editor the number belongs to).</summary>
public readonly record struct PlacementPick(int CreationNumber, bool IsUnit);

/// <summary>
/// Experimental GPU terrain viewport: renders the map's heightmap as a lit,
/// per-terrain-type coloured mesh via Silk.NET on Avalonia's
/// <see cref="OpenGlControlBase"/>. The mesh is built on the UI thread by
/// <see cref="TerrainMeshBuilder"/> and uploaded lazily on the GL thread.
/// </summary>
public sealed class TerrainGlView : OpenGlControlBase
{
    private static readonly string LogPath =
        Path.Combine(AppContext.BaseDirectory, "wc3studio-gl.log");
    private static bool s_logged;

    private GL? _gl;
    private uint _program, _vao, _vbo, _ebo, _texArray;
    private int _uMvp = -1, _uLight = -1, _uTiles = -1;
    private bool _initialized;

    // CPU-side mesh, uploaded to the GPU on the GL thread when dirty.
    private float[]? _verts;
    private uint[]? _indices;
    private int _indexCount;
    private bool _meshDirty;

    // Water surface (position-only quads over water cells, built by WaterMeshBuilder),
    // uploaded like the terrain mesh and drawn as a translucent pass after it so
    // existing and sculpted water shows in the 3D view.
    private uint _waterProgram, _waterVao, _waterVbo, _waterEbo;
    private int _uWaterMvp = -1, _uWaterColor = -1;
    private float[]? _waterVerts;
    private uint[]? _waterIndices;
    private int _waterIndexCount;
    private bool _waterDirty;

    // Water surface tint: the 2D perspective renderer's water colour (48, 96, 168)
    // normalized to 0..1, drawn at the same 0.55 alpha, so both views agree.
    private const float WaterR = 48f / 255f, WaterG = 96f / 255f, WaterB = 168f / 255f, WaterA = 0.55f;

    // Placement scene (units/doodads as real textured models, box fallback): each unique
    // mesh + its textures upload to the GPU once and draw per instance with a per-instance
    // world transform + owner colour, honoring each section's material filter mode
    // (alpha-cutout only for Transparent, additive blending for glows) and painting
    // team-colour sections with the owning player's colour.
    private uint _placeProgram;
    private int _uPlaceVP = -1, _uPlaceModel = -1, _uPlaceLight = -1, _uPlaceTex = -1, _uPlaceUseTex = -1;
    private int _uPlaceFilter = -1, _uPlaceTeamColor = -1, _uPlaceOwnerColor = -1, _uPlaceHighlight = -1;
    private PlacementSceneData? _scene;
    private bool _sceneDirty;
    private readonly Dictionary<string, GpuMesh> _gpuMeshes = new();
    private readonly Dictionary<string, List<PlacementInstance>> _instanceGroups = new();

    // Creation numbers of the selected units (set by a viewport click/marquee or by a
    // panel via SetSelectedUnits); matching instances draw highlighted. Empty = no
    // selection. The reference is swapped whole (never mutated) so the render pass can
    // read it without locking — the same idiom as _scene.
    private HashSet<int> _selectedUnits = new();

    // Creation number of the selected doodad (single-select only), or null for none.
    // Kept separate from _selectedUnits because the two creation-number spaces overlap.
    private int? _selectedDoodad;

    // Anisotropy to apply to textures: min(8, GL max), lazily queried once per context;
    // 0 = unsupported (no-op), -1 = not queried yet.
    private float _anisotropy = -1f;

    private sealed record GpuMesh(
        uint Vao, uint Vbo, uint Ebo, uint[] Textures, IReadOnlyList<PlacementMeshSection> Sections);

    // Source document + height field, kept so placements can be re-read (RefreshPlacements)
    // and GL clicks ray-picked against the terrain (PickGround). The resolver maps a
    // placement type (rawcode, isUnit) to its render model; null falls back to boxes.
    private Wc3.Model.MapDocument? _doc;
    private TerrainHeightField? _heights;
    private Func<string, bool, PlacementModel?>? _modelResolver;

    // CPU-side terrain-type tile textures (RGBA layers), uploaded as a 2D array when dirty.
    private byte[]? _layerData;
    private int _layerCount;
    private int _layerCell = TerrainArtCatalog.Cell;
    private bool _texDirty;

    // Orbit camera (yaw spins around Z, pitch tilts down, zoom scales distance).
    private float _yaw = 45f, _pitch = 30f, _zoom = 1f;
    private Vector3 _center;
    private float _radius = 512f;

    // Look-target offset from the mesh centre (world units); driven by the input overlay.
    private Vector3 _pan;

    /// <summary>Loads a map's terrain + placements into the viewport (safe to call off the
    /// GL thread). <paramref name="modelResolver"/> supplies real render models per placement
    /// type ((rawcode, isUnit) → model, null = kind-colored box); it should cache per type.</summary>
    // Bumped on every SetMap so a build that finishes after a newer map was opened is discarded.
    private int _buildGen;
    // Model resolution reads the shared game-data (CASC), which is not thread-safe, so at most one
    // terrain build runs at a time across the app.
    private static readonly object BuildLock = new();

    /// <summary>
    /// Points the viewport at a map. The terrain mesh, tile textures, and the placement scene
    /// (which parses an MDX model and its textures for every placed unit and doodad) are pure CPU
    /// work, the GL upload happens later through the dirty flags, so all of it runs OFF the UI
    /// thread and is applied back on it when ready. Opening a big, heavily placed map no longer
    /// freezes the app, the viewport clears immediately and repopulates a moment later.
    /// <paramref name="onReady"/> runs on the UI thread after the build applies (used to frame the
    /// camera, which needs the built mesh's center and radius).
    /// </summary>
    public void SetMap(
        Wc3.Model.MapDocument? doc,
        Func<string, bool, PlacementModel?>? modelResolver = null,
        Action? onReady = null)
    {
        _doc = doc;
        _modelResolver = modelResolver;
        int gen = ++_buildGen;

        // Clear the current scene at once so the old map does not linger while the new one builds.
        _verts = null; _indices = null; _indexCount = 0;
        _waterVerts = null; _waterIndices = null; _waterIndexCount = 0;
        _heights = null; _scene = null;
        _meshDirty = _waterDirty = _texDirty = _sceneDirty = true;
        RequestNextFrameRendering();
        if (doc is null) return;

        Task.Run(() =>
        {
            lock (BuildLock)
            {
                if (gen != _buildGen) return; // a newer map superseded this one while it was queued
                try
                {
                    var mesh = TerrainMeshBuilder.Build(doc);
                    var water = WaterMeshBuilder.Build(doc);
                    var layerData = TerrainArtCatalog.BuildLayersForMap(doc, out var layerCount, out var layerCell);
                    var heights = TerrainHeightField.TryCreate(doc);
                    var scene = PlacementScene.Build(doc, modelResolver);

                    Dispatcher.UIThread.Post(() =>
                    {
                        if (gen != _buildGen) return; // superseded between build end and apply
                        _verts = mesh.Vertices; _indices = mesh.Indices;
                        _center = mesh.Center; _radius = mesh.Radius; _meshDirty = true;
                        _waterVerts = water.Vertices; _waterIndices = water.Indices; _waterDirty = true;
                        _layerData = layerData; _layerCount = layerCount; _layerCell = layerCell; _texDirty = true;
                        _heights = heights;
                        _scene = scene; _sceneDirty = true;
                        RequestNextFrameRendering();
                        onReady?.Invoke();
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Post(() => { if (gen == _buildGen) Log($"SetMap failed: {ex.Message}"); });
                }
            }
        });
    }

    /// <summary>
    /// Re-reads the map's unit/doodad placements and rebuilds their geometry. Call after
    /// any placement edit / undo / redo so the viewport tracks the live widget set.
    /// </summary>
    public void RefreshPlacements()
    {
        try
        {
            _scene = _doc is null ? null : PlacementScene.Build(_doc, _modelResolver);
            _sceneDirty = true;
            RequestNextFrameRendering();
        }
        catch (Exception ex)
        {
            Log($"RefreshPlacements failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Re-reads the map's terrain from the (already mutated) document and re-uploads the
    /// height mesh, mirroring <see cref="RefreshPlacements"/>. Call after a sculpt edit to
    /// war3map.w3e. Also rebuilds the water surface (so a Water sculpt shows immediately),
    /// the height field (so ground picks land on the new surface) and the placement scene
    /// (so widgets ride the new ground Z). The camera framing (centre/radius) is
    /// intentionally kept so strokes never jump the view, and the tile texture array is
    /// untouched because the map's tile-type list cannot change here.
    /// </summary>
    public void RefreshTerrain()
    {
        try
        {
            if (_doc is null)
                return;
            var mesh = TerrainMeshBuilder.Build(_doc);
            _verts = mesh.Vertices;
            _indices = mesh.Indices;
            _meshDirty = true;
            var water = WaterMeshBuilder.Build(_doc);
            _waterVerts = water.Vertices;
            _waterIndices = water.Indices;
            _waterDirty = true;
            _heights = TerrainHeightField.TryCreate(_doc);
            _scene = PlacementScene.Build(_doc, _modelResolver);
            _sceneDirty = true;
            RequestNextFrameRendering();
        }
        catch (Exception ex)
        {
            Log($"RefreshTerrain failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Ray-picks the terrain under a viewport point (in this control's logical
    /// coordinates, e.g. from the GlInput overlay) using the exact camera the last
    /// frame rendered with. Returns the world (x, y) the ray strikes.
    /// </summary>
    public (bool ok, float wx, float wy) PickGround(double px, double py)
    {
        if (_heights is null || !TryGetPickRay(px, py, out var eye, out var dir))
            return (false, 0f, 0f);
        return _heights.RaycastGround(eye, dir);
    }

    /// <summary>
    /// Ray-picks the placed widget (unit or doodad) under a viewport point (same
    /// camera/ray as <see cref="PickGround"/>): tests the ray against every instance's
    /// world-space AABB (its mesh's local bounds carried through the instance transform)
    /// and returns the creation number of the nearest hit along the ray, tagged with
    /// whether it is a unit or a doodad, or null when the click hits nothing.
    /// </summary>
    public PlacementPick? PickPlacement(double px, double py)
    {
        var scene = _scene;
        if (scene is null || !TryGetPickRay(px, py, out var eye, out var dir))
            return null;

        var meshByKey = new Dictionary<string, PlacementMesh>(scene.Meshes.Count);
        foreach (var mesh in scene.Meshes)
            meshByKey[mesh.Key] = mesh;

        PlacementPick? best = null;
        float bestT = float.MaxValue;
        foreach (var inst in scene.Instances)
        {
            if (inst.CreationNumber is not int cn ||
                !meshByKey.TryGetValue(inst.MeshKey, out var mesh))
                continue;

            // Local AABB -> world AABB: transform all 8 corners so rotation/scale in the
            // instance transform still yields a conservative axis-aligned box.
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? mesh.BoundsMin.X : mesh.BoundsMax.X,
                    (i & 2) == 0 ? mesh.BoundsMin.Y : mesh.BoundsMax.Y,
                    (i & 4) == 0 ? mesh.BoundsMin.Z : mesh.BoundsMax.Z);
                var world = Vector3.Transform(corner, inst.World);
                min = Vector3.Min(min, world);
                max = Vector3.Max(max, world);
            }

            if (RayHitsAabb(eye, dir, min, max, out float t) && t < bestT)
            {
                bestT = t;
                best = new PlacementPick(cn, inst.IsUnit);
            }
        }
        return best;
    }

    /// <summary>Stores the creation numbers of the units the viewport should draw
    /// highlighted (an empty collection clears the highlight) and schedules a redraw.
    /// Fed by the viewport's own click/marquee picks and by panel-driven selection.</summary>
    public void SetSelectedUnits(IReadOnlyCollection<int> creationNumbers)
    {
        var next = new HashSet<int>(creationNumbers);
        if (next.SetEquals(_selectedUnits))
            return;
        _selectedUnits = next; // swap, don't mutate: the render pass reads the old set lock-free
        Log(next.Count > 0 ? $"selected {next.Count} unit(s)" : "selection cleared");
        RequestNextFrameRendering();
    }

    /// <summary>Single-unit convenience over <see cref="SetSelectedUnits"/>: highlights
    /// exactly that unit (null clears the highlight).</summary>
    public void SetSelectedUnit(int? creationNumber) =>
        SetSelectedUnits(creationNumber is int cn ? new[] { cn } : Array.Empty<int>());

    /// <summary>Stores the creation number of the doodad the viewport should draw
    /// highlighted (null clears it) and schedules a redraw. Doodad selection is
    /// single-pick only, unlike the unit set.</summary>
    public void SetSelectedDoodad(int? creationNumber)
    {
        if (_selectedDoodad == creationNumber)
            return;
        _selectedDoodad = creationNumber;
        Log(creationNumber is int cn ? $"selected doodad #{cn}" : "doodad selection cleared");
        RequestNextFrameRendering();
    }

    /// <summary>
    /// Picks every placed unit whose screen position falls inside the marquee rectangle
    /// (viewport logical coordinates; any two opposite corners). Each unit instance's
    /// representative point — its mesh-AABB centre carried through the instance
    /// transform, or the raw world translation for meshes not in the scene — is
    /// projected with the exact camera the last frame rendered with; units behind the
    /// camera never match. Doodads are excluded, the marquee is a unit multi-select
    /// (a doodad is picked one at a time via <see cref="PickPlacement"/>).
    /// </summary>
    public IReadOnlyList<int> PickUnitsInRect(double x0, double y0, double x1, double y1)
    {
        var result = new List<int>();
        var scene = _scene;
        double w = Bounds.Width, h = Bounds.Height;
        if (scene is null || w <= 0 || h <= 0)
            return result;

        double minX = Math.Min(x0, x1), maxX = Math.Max(x0, x1);
        double minY = Math.Min(y0, y1), maxY = Math.Max(y0, y1);
        var viewProj = BuildViewProjection(w, h);

        var meshByKey = new Dictionary<string, PlacementMesh>(scene.Meshes.Count);
        foreach (var mesh in scene.Meshes)
            meshByKey[mesh.Key] = mesh;

        foreach (var inst in scene.Instances)
        {
            if (!inst.IsUnit || inst.CreationNumber is not int cn)
                continue;
            var world = meshByKey.TryGetValue(inst.MeshKey, out var mesh)
                ? Vector3.Transform((mesh.BoundsMin + mesh.BoundsMax) * 0.5f, inst.World)
                : inst.World.Translation;
            var (sx, sy, inFront) = WorldToScreen(world, viewProj, w, h);
            if (inFront && sx >= minX && sx <= maxX && sy >= minY && sy <= maxY)
                result.Add(cn);
        }
        return result;
    }

    /// <summary>
    /// The view*projection the render pass uses, rebuilt from the current camera state
    /// for the given viewport size (logical units are fine — the aspect ratio matches
    /// the pixel viewport). Mirrors OnOpenGlRender exactly so picking agrees with what
    /// is on screen.
    /// </summary>
    private Matrix4x4 BuildViewProjection(double w, double h)
    {
        float yaw = _yaw * (MathF.PI / 180f);
        float pitch = Math.Clamp(_pitch, 2f, 89f) * (MathF.PI / 180f);
        var eyeDir = new Vector3(
            MathF.Cos(pitch) * MathF.Cos(yaw),
            MathF.Cos(pitch) * MathF.Sin(yaw),
            MathF.Sin(pitch));
        float dist = _radius / MathF.Max(_zoom, 0.05f) * 2.4f;
        var target = _center + _pan;
        var eye = target + eyeDir * dist;
        var view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitZ);
        float aspect = (float)(w / h);
        float near = MathF.Max(dist * 0.02f, 1f);
        float far = dist * 4f + _radius * 2f;
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(48f * MathF.PI / 180f, aspect, near, far);
        return view * proj;
    }

    /// <summary>Projects a world point through the render camera to viewport logical
    /// coordinates. <c>inFront</c> is false when the point is at/behind the eye plane
    /// (the projected x/y are then meaningless).</summary>
    private static (double x, double y, bool inFront) WorldToScreen(
        Vector3 world, in Matrix4x4 viewProj, double w, double h)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), viewProj);
        if (clip.W <= 1e-5f)
            return (0, 0, false);
        double ndcX = clip.X / clip.W, ndcY = clip.Y / clip.W;
        return ((ndcX * 0.5 + 0.5) * w, (0.5 - ndcY * 0.5) * h, true);
    }

    /// <summary>
    /// Builds the world-space pick ray through a viewport point (in this control's
    /// logical coordinates) using the exact camera the last frame rendered with.
    /// Shared by <see cref="PickGround"/> and <see cref="PickPlacement"/>.
    /// </summary>
    private bool TryGetPickRay(double px, double py, out Vector3 eye, out Vector3 dir)
    {
        eye = default;
        dir = default;
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0)
            return false;

        // Same camera as OnOpenGlRender (logical units: aspect and pixel angles match).
        float yaw = _yaw * (MathF.PI / 180f);
        float pitch = Math.Clamp(_pitch, 2f, 89f) * (MathF.PI / 180f);
        var eyeDir = new Vector3(
            MathF.Cos(pitch) * MathF.Cos(yaw),
            MathF.Cos(pitch) * MathF.Sin(yaw),
            MathF.Sin(pitch));
        float dist = _radius / MathF.Max(_zoom, 0.05f) * 2.4f;
        var target = _center + _pan;
        eye = target + eyeDir * dist;

        var forward = -eyeDir;
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
        var up = Vector3.Cross(right, forward);
        float focal = 0.5f * (float)h / MathF.Tan(0.5f * (48f * MathF.PI / 180f));

        float a = ((float)px - (float)w * 0.5f) / focal;
        float b = ((float)h * 0.5f - (float)py) / focal;
        dir = Vector3.Normalize(forward + a * right + b * up);
        return true;
    }

    /// <summary>Slab-tests a ray against a world-space AABB. On a hit, <paramref name="t"/>
    /// is the entry distance along the ray (0 when the eye starts inside the box); only
    /// hits in front of the eye count.</summary>
    private static bool RayHitsAabb(Vector3 origin, Vector3 dir, Vector3 min, Vector3 max, out float t)
    {
        float tMin = 0f, tMax = float.MaxValue;
        if (SlabHit(origin.X, dir.X, min.X, max.X, ref tMin, ref tMax) &&
            SlabHit(origin.Y, dir.Y, min.Y, max.Y, ref tMin, ref tMax) &&
            SlabHit(origin.Z, dir.Z, min.Z, max.Z, ref tMin, ref tMax))
        {
            t = tMin;
            return true;
        }
        t = 0f;
        return false;
    }

    private static bool SlabHit(float o, float d, float min, float max, ref float tMin, ref float tMax)
    {
        if (MathF.Abs(d) < 1e-8f)
            return o >= min && o <= max; // ray parallel to the slab: inside it or a miss
        float t1 = (min - o) / d, t2 = (max - o) / d;
        if (t1 > t2) (t1, t2) = (t2, t1);
        tMin = MathF.Max(tMin, t1);
        tMax = MathF.Min(tMax, t2);
        return tMin <= tMax;
    }

    /// <summary>Sets the orbit-camera angle to match the 2D perspective view.</summary>
    public void SetCamera(float yaw, float pitch, float zoom)
    {
        _yaw = yaw; _pitch = pitch; _zoom = zoom;
        RequestNextFrameRendering();
    }

    // Pointer/key input is handled by TerrainView's transparent GlInput overlay
    // (OpenGlControlBase isn't hit-test-visible) and forwarded via the Drive* API below.

    private void OrbitBy(double dx, double dy)
    {
        _yaw -= (float)dx * 0.4f;
        _pitch = Math.Clamp(_pitch + (float)dy * 0.4f, 2f, 89f);
    }

    private void PanBy(double dx, double dy)
    {
        // Screen-parallel pan; scale by distance so it feels consistent at any zoom.
        float dist = _radius / MathF.Max(_zoom, 0.05f) * 2.4f;
        float scale = dist * 0.0015f;
        var (right, _, up) = CameraBasis();
        _pan += (right * (float)(-dx) + up * (float)dy) * scale;
    }

    private void PanWorld(float rightAmt, float fwdAmt)
    {
        // Ground-plane pan (arrow keys): move target along camera's ground axes.
        var (right, groundFwd, _) = CameraBasis();
        _pan += right * rightAmt + groundFwd * fwdAmt;
    }

    // --- external input drive -------------------------------------------------
    // Avalonia's OpenGlControlBase renders through a custom draw operation that is
    // NOT hit-test-visible, so this control never receives pointer input directly
    // (its OnPointer* overrides never fire). TerrainView overlays a transparent
    // hit-catching surface and forwards gestures through these public methods, which
    // also schedule the redraw (RequestNextFrameRendering is protected).

    /// <summary>Orbit the camera by a screen drag delta (yaw spin / pitch tilt).</summary>
    public void DriveOrbit(double dx, double dy) { OrbitBy(dx, dy); RequestNextFrameRendering(); }

    /// <summary>Pan the look-target parallel to the screen by a drag delta.</summary>
    public void DrivePan(double dx, double dy) { PanBy(dx, dy); RequestNextFrameRendering(); }

    /// <summary>Dolly the camera in/out by a wheel notch (positive = zoom in).</summary>
    public void DriveZoom(double wheelDelta)
    {
        if (wheelDelta == 0) return; // horizontal / trackpad-X scroll: don't drift zoom out
        float factor = wheelDelta > 0 ? 1.1f : 1f / 1.1f;
        _zoom = Math.Clamp(_zoom * factor, 0.05f, 20f);
        RequestNextFrameRendering();
    }

    /// <summary>Nudge the look-target along the ground plane (arrow-key style pan).</summary>
    public void DrivePanWorld(float rightAmt, float fwdAmt) { PanWorld(rightAmt, fwdAmt); RequestNextFrameRendering(); }

    /// <summary>Reset the orbit camera to its default framing.</summary>
    public void DriveReset()
    {
        _pan = Vector3.Zero; _yaw = 45f; _pitch = 30f; _zoom = 1f;
        RequestNextFrameRendering();
    }

    /// <summary>Pan step for arrow-key navigation, ~8% of the map radius.</summary>
    public float PanStep => _radius * 0.08f;

    /// <summary>Camera right / ground-forward / up basis for the current yaw/pitch.</summary>
    private (Vector3 right, Vector3 groundFwd, Vector3 up) CameraBasis()
    {
        float yaw = _yaw * (MathF.PI / 180f);
        float pitch = Math.Clamp(_pitch, 2f, 89f) * (MathF.PI / 180f);
        var eyeDir = new Vector3(
            MathF.Cos(pitch) * MathF.Cos(yaw),
            MathF.Cos(pitch) * MathF.Sin(yaw),
            MathF.Sin(pitch));
        var forward = -eyeDir;                                        // eye -> target
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
        var up = Vector3.Cross(right, forward);
        var groundFwd = new Vector3(forward.X, forward.Y, 0f);
        if (groundFwd.LengthSquared() > 1e-6f) groundFwd = Vector3.Normalize(groundFwd);
        return (right, groundFwd, up);
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        try
        {
            _gl = GL.GetApi(new LamdaNativeContext(gl.GetProcAddress));

            if (!s_logged)
            {
                s_logged = true;
                Log($"GL init: {GlVersion.Type} {GlVersion.Major}.{GlVersion.Minor} | " +
                    $"version='{_gl.GetStringS(StringName.Version)}' " +
                    $"vendor='{_gl.GetStringS(StringName.Vendor)}' " +
                    $"renderer='{_gl.GetStringS(StringName.Renderer)}' " +
                    $"glsl='{_gl.GetStringS(StringName.ShadingLanguageVersion)}'");
            }

            bool es = GlVersion.Type == GlProfileType.OpenGLES;
            string header = es ? "#version 300 es\nprecision highp float;\n" : "#version 330 core\n";

            string vsSrc = header +
                "layout(location=0) in vec3 aPos;\n" +
                "layout(location=1) in vec3 aNormal;\n" +
                "layout(location=2) in vec2 aUV;\n" +
                "layout(location=3) in vec4 aLayers;\n" +
                "uniform mat4 uMVP;\n" +
                "out vec3 vNormal;\n" +
                "out vec2 vUV;\n" +
                "flat out vec4 vLayers;\n" +
                "void main(){ vNormal = aNormal; vUV = aUV; vLayers = aLayers; gl_Position = uMVP * vec4(aPos, 1.0); }\n";

            string fsSrc = header +
                (es ? "precision highp sampler2DArray;\n" : "") +
                "in vec3 vNormal;\n" +
                "in vec2 vUV;\n" +
                "flat in vec4 vLayers;\n" +
                "uniform vec3 uLight;\n" +
                "uniform sampler2DArray uTiles;\n" +
                "out vec4 fragColor;\n" +
                "void main(){\n" +
                "  vec2 w = smoothstep(0.0, 1.0, vUV);\n" +
                "  vec4 c00 = texture(uTiles, vec3(vUV, vLayers.x), -0.35);\n" +
                "  vec4 c10 = texture(uTiles, vec3(vUV, vLayers.y), -0.35);\n" +
                "  vec4 c01 = texture(uTiles, vec3(vUV, vLayers.z), -0.35);\n" +
                "  vec4 c11 = texture(uTiles, vec3(vUV, vLayers.w), -0.35);\n" +
                "  vec3 tex = mix(mix(c00.rgb, c10.rgb, w.x), mix(c01.rgb, c11.rgb, w.x), w.y);\n" +
                "  float d = max(dot(normalize(vNormal), normalize(uLight)), 0.0);\n" +
                "  float shade = 0.35 + 0.65 * d;\n" +
                "  fragColor = vec4(tex * shade, 1.0);\n" +
                "}\n";

            uint vs = CompileShader(_gl, ShaderType.VertexShader, vsSrc);
            uint fs = CompileShader(_gl, ShaderType.FragmentShader, fsSrc);
            _program = _gl.CreateProgram();
            _gl.AttachShader(_program, vs);
            _gl.AttachShader(_program, fs);
            _gl.LinkProgram(_program);
            _gl.GetProgram(_program, ProgramPropertyARB.LinkStatus, out int linkOk);
            if (linkOk == 0)
                Log("program link failed: " + _gl.GetProgramInfoLog(_program));
            _gl.DetachShader(_program, vs);
            _gl.DetachShader(_program, fs);
            _gl.DeleteShader(vs);
            _gl.DeleteShader(fs);

            _uMvp = _gl.GetUniformLocation(_program, "uMVP");
            _uLight = _gl.GetUniformLocation(_program, "uLight");
            _uTiles = _gl.GetUniformLocation(_program, "uTiles");

            _vao = _gl.GenVertexArray();
            _vbo = _gl.GenBuffer();
            _ebo = _gl.GenBuffer();

            // Placement program: unique meshes (real textured models / fallback boxes)
            // drawn once per instance with a world-transform uniform. Textured sections
            // sample the geoset's texture UV-mapped, alpha-cutting ONLY when the
            // material's filter mode is Transparent (WC3 foliage/hair) — opaque modes
            // must not discard or unit bodies get holes; Additive sections blend onto
            // the frame (handled by GL blend state in the draw loop). Team-colour
            // sections paint the owning player's colour; other untextured sections use
            // the flat vertex color; the selected unit draws brightened with a warm
            // tint. Lighting is two-sided lambert (|dot|) like the CPU ModelRenderer —
            // WC3 materials are frequently two-sided and culling is off.
            string pvsSrc = header +
                "layout(location=0) in vec3 aPos;\n" +
                "layout(location=1) in vec3 aNormal;\n" +
                "layout(location=2) in vec2 aUV;\n" +
                "layout(location=3) in vec3 aColor;\n" +
                "uniform mat4 uVP;\n" +
                "uniform mat4 uModel;\n" +
                "out vec3 vNormal;\n" +
                "out vec2 vUV;\n" +
                "out vec3 vColor;\n" +
                "void main(){ vNormal = mat3(uModel) * aNormal; vUV = aUV; vColor = aColor; " +
                "gl_Position = uVP * (uModel * vec4(aPos, 1.0)); }\n";
            string pfsSrc = header +
                "in vec3 vNormal;\n" +
                "in vec2 vUV;\n" +
                "in vec3 vColor;\n" +
                "uniform vec3 uLight;\n" +
                "uniform sampler2D uTex;\n" +
                "uniform int uUseTex;\n" +
                "uniform int uFilter;\n" +      // Wc3.Modeling.FilterMode ordinal: 1=Transparent 2=Blend 3=Additive 4=AddAlpha 5=Modulate 6=Modulate2x
                "uniform int uTeamColor;\n" +   // untextured section painted with the owner's colour
                "uniform vec3 uOwnerColor;\n" +
                "uniform int uHighlight;\n" +   // selected unit: brighten + warm tint
                "out vec4 fragColor;\n" +
                "void main(){\n" +
                "  vec3 base = (uTeamColor == 1) ? uOwnerColor : vColor;\n" +
                "  float alpha = 1.0;\n" +
                "  if (uUseTex == 1) {\n" +
                "    vec4 t = texture(uTex, vUV);\n" +
                "    if (uFilter == 1 && t.a < 0.5) discard;\n" +   // cutout only for Transparent (leaves, hair)
                "    base = t.rgb;\n" +
                "    alpha = t.a;\n" +
                "  }\n" +
                "  float shade = 1.0;\n" +
                "  float nlen = dot(vNormal, vNormal);\n" +
                "  if (nlen > 1e-8) shade = 0.4 + 0.6 * abs(dot(vNormal / sqrt(nlen), normalize(uLight)));\n" +
                "  vec3 rgb = base * shade;\n" +
                "  if (uHighlight == 1) rgb = mix(rgb * 1.4, vec3(1.0, 0.82, 0.25), 0.28);\n" +
                // Blend(2)/Additive(3)/AddAlpha(4) modulate by the texture alpha through the
                // per-section GL blend func; opaque modes (0/1) and Modulate(5/6, which blend on
                // colour) output alpha 1.
                "  float outA = (uFilter == 2 || uFilter == 3 || uFilter == 4) ? alpha : 1.0;\n" +
                "  fragColor = vec4(rgb, outA);\n" +
                "}\n";
            uint pvs = CompileShader(_gl, ShaderType.VertexShader, pvsSrc);
            uint pfs = CompileShader(_gl, ShaderType.FragmentShader, pfsSrc);
            _placeProgram = _gl.CreateProgram();
            _gl.AttachShader(_placeProgram, pvs);
            _gl.AttachShader(_placeProgram, pfs);
            _gl.LinkProgram(_placeProgram);
            _gl.GetProgram(_placeProgram, ProgramPropertyARB.LinkStatus, out int placeLinkOk);
            if (placeLinkOk == 0)
                Log("placement program link failed: " + _gl.GetProgramInfoLog(_placeProgram));
            _gl.DetachShader(_placeProgram, pvs);
            _gl.DetachShader(_placeProgram, pfs);
            _gl.DeleteShader(pvs);
            _gl.DeleteShader(pfs);
            _uPlaceVP = _gl.GetUniformLocation(_placeProgram, "uVP");
            _uPlaceModel = _gl.GetUniformLocation(_placeProgram, "uModel");
            _uPlaceLight = _gl.GetUniformLocation(_placeProgram, "uLight");
            _uPlaceTex = _gl.GetUniformLocation(_placeProgram, "uTex");
            _uPlaceUseTex = _gl.GetUniformLocation(_placeProgram, "uUseTex");
            _uPlaceFilter = _gl.GetUniformLocation(_placeProgram, "uFilter");
            _uPlaceTeamColor = _gl.GetUniformLocation(_placeProgram, "uTeamColor");
            _uPlaceOwnerColor = _gl.GetUniformLocation(_placeProgram, "uOwnerColor");
            _uPlaceHighlight = _gl.GetUniformLocation(_placeProgram, "uHighlight");

            // Water program: bare positions through the same MVP, one flat translucent
            // colour. The terrain shader wants normals, UVs and the tile texture array,
            // none of which a flat water sheet carries, so a tiny dedicated program is
            // cleaner than reusing it.
            string wvsSrc = header +
                "layout(location=0) in vec3 aPos;\n" +
                "uniform mat4 uMVP;\n" +
                "void main(){ gl_Position = uMVP * vec4(aPos, 1.0); }\n";
            string wfsSrc = header +
                "uniform vec4 uColor;\n" +
                "out vec4 fragColor;\n" +
                "void main(){ fragColor = uColor; }\n";
            uint wvs = CompileShader(_gl, ShaderType.VertexShader, wvsSrc);
            uint wfs = CompileShader(_gl, ShaderType.FragmentShader, wfsSrc);
            _waterProgram = _gl.CreateProgram();
            _gl.AttachShader(_waterProgram, wvs);
            _gl.AttachShader(_waterProgram, wfs);
            _gl.LinkProgram(_waterProgram);
            _gl.GetProgram(_waterProgram, ProgramPropertyARB.LinkStatus, out int waterLinkOk);
            if (waterLinkOk == 0)
                Log("water program link failed: " + _gl.GetProgramInfoLog(_waterProgram));
            _gl.DetachShader(_waterProgram, wvs);
            _gl.DetachShader(_waterProgram, wfs);
            _gl.DeleteShader(wvs);
            _gl.DeleteShader(wfs);
            _uWaterMvp = _gl.GetUniformLocation(_waterProgram, "uMVP");
            _uWaterColor = _gl.GetUniformLocation(_waterProgram, "uColor");

            _waterVao = _gl.GenVertexArray();
            _waterVbo = _gl.GenBuffer();
            _waterEbo = _gl.GenBuffer();

            _initialized = true;
            _meshDirty = _verts is not null;
            _texDirty = _layerData is not null;
            _sceneDirty = _scene is not null;
            _waterDirty = _waterVerts is not null;
        }
        catch (Exception ex)
        {
            Log($"OnOpenGlInit failed: {ex.Message}");
        }
    }

    protected override unsafe void OnOpenGlRender(GlInterface gl, int fb)
    {
        try
        {
            if (_gl is null || !_initialized) return;

            double scaling = (VisualRoot as TopLevel)?.RenderScaling ?? 1.0;
            int pxW = Math.Max(1, (int)(Bounds.Width * scaling));
            int pxH = Math.Max(1, (int)(Bounds.Height * scaling));
            _gl.Viewport(0, 0, (uint)pxW, (uint)pxH);

            _gl.Enable(EnableCap.DepthTest);
            _gl.Disable(EnableCap.CullFace);
            _gl.ClearColor(0.11f, 0.125f, 0.149f, 1f);
            _gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));

            if (_meshDirty) UploadMesh();
            if (_waterDirty) UploadWaterMesh();
            if (_texDirty) UploadTexArray();
            if (_sceneDirty) SyncPlacementScene();
            bool hasPlacements = _instanceGroups.Count > 0;
            if (_indexCount == 0 && !hasPlacements && _waterIndexCount == 0) return;

            // Orbit camera around the scene centre (matches RenderPerspectivePng).
            float yaw = _yaw * (MathF.PI / 180f);
            float pitch = Math.Clamp(_pitch, 2f, 89f) * (MathF.PI / 180f);
            var eyeDir = new Vector3(
                MathF.Cos(pitch) * MathF.Cos(yaw),
                MathF.Cos(pitch) * MathF.Sin(yaw),
                MathF.Sin(pitch));
            float dist = _radius / MathF.Max(_zoom, 0.05f) * 2.4f;
            var target = _center + _pan;
            var eye = target + eyeDir * dist;

            var view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitZ);
            float aspect = (float)pxW / pxH;
            float near = MathF.Max(dist * 0.02f, 1f);
            float far = dist * 4f + _radius * 2f;
            var proj = Matrix4x4.CreatePerspectiveFieldOfView(48f * MathF.PI / 180f, aspect, near, far);
            var mvp = view * proj;

            var light = Vector3.Normalize(eyeDir + new Vector3(-0.3f, -0.2f, 0.7f));

            if (_indexCount > 0)
            {
                _gl.UseProgram(_program);
                if (_texArray != 0)
                {
                    _gl.ActiveTexture(TextureUnit.Texture0);
                    _gl.BindTexture(TextureTarget.Texture2DArray, _texArray);
                    if (_uTiles >= 0) _gl.Uniform1(_uTiles, 0);
                }
                if (_uLight >= 0) _gl.Uniform3(_uLight, light.X, light.Y, light.Z);
                if (_uMvp >= 0)
                    _gl.UniformMatrix4(_uMvp, 1, false, MemoryMarshal.CreateReadOnlySpan(ref mvp.M11, 16));

                _gl.BindVertexArray(_vao);
                _gl.DrawElements(PrimitiveType.Triangles, (uint)_indexCount, DrawElementsType.UnsignedInt, (void*)0);
                _gl.BindVertexArray(0);
            }

            // Placements: every unique mesh drawn once per instance, depth-tested so
            // hills occlude units behind them. Loop nesting binds each section's
            // texture + filter state once and then stamps all instances of the mesh
            // through it, with per-instance owner colour and selection highlight.
            if (hasPlacements && _placeProgram != 0)
            {
                _gl.UseProgram(_placeProgram);
                if (_uPlaceVP >= 0)
                    _gl.UniformMatrix4(_uPlaceVP, 1, false, MemoryMarshal.CreateReadOnlySpan(ref mvp.M11, 16));
                if (_uPlaceLight >= 0) _gl.Uniform3(_uPlaceLight, light.X, light.Y, light.Z);
                if (_uPlaceTex >= 0) _gl.Uniform1(_uPlaceTex, 1); // unit 1; terrain array owns unit 0

                var selected = _selectedUnits; // snapshot: SetSelectedUnits swaps the reference
                var selectedDoodad = _selectedDoodad;
                foreach (var (key, instances) in _instanceGroups)
                {
                    if (!_gpuMeshes.TryGetValue(key, out var gpuMesh))
                        continue;
                    _gl.BindVertexArray(gpuMesh.Vao);
                    foreach (var section in gpuMesh.Sections)
                    {
                        bool useTex = section.TextureSlot >= 0 && section.TextureSlot < gpuMesh.Textures.Length;
                        if (_uPlaceUseTex >= 0) _gl.Uniform1(_uPlaceUseTex, useTex ? 1 : 0);
                        if (_uPlaceFilter >= 0) _gl.Uniform1(_uPlaceFilter, (int)section.FilterMode);
                        if (useTex)
                        {
                            _gl.ActiveTexture(TextureUnit.Texture1);
                            _gl.BindTexture(TextureTarget.Texture2D, gpuMesh.Textures[section.TextureSlot]);
                        }

                        // Non-opaque sections (glows, capes, soft foliage, shadows) must blend,
                        // not overwrite — otherwise they render as a hard "weird polygon". Each
                        // WC3 filter mode maps to its GL blend func; all are depth-tested but not
                        // depth-written so geometry behind them still draws. None(0)/Transparent(1)
                        // stay opaque (Transparent alpha-tests in the shader).
                        var fm = section.FilterMode;
                        bool translucent = fm is Wc3.Modeling.FilterMode.Blend
                            or Wc3.Modeling.FilterMode.Additive
                            or Wc3.Modeling.FilterMode.AddAlpha
                            or Wc3.Modeling.FilterMode.Modulate
                            or Wc3.Modeling.FilterMode.Modulate2x;
                        if (translucent)
                        {
                            _gl.Enable(EnableCap.Blend);
                            _gl.DepthMask(false);
                            switch (fm)
                            {
                                case Wc3.Modeling.FilterMode.Blend:
                                    _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                                    break;
                                case Wc3.Modeling.FilterMode.Additive:
                                case Wc3.Modeling.FilterMode.AddAlpha:
                                    _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
                                    break;
                                case Wc3.Modeling.FilterMode.Modulate:
                                    _gl.BlendFunc(BlendingFactor.Zero, BlendingFactor.SrcColor);
                                    break;
                                case Wc3.Modeling.FilterMode.Modulate2x:
                                    _gl.BlendFunc(BlendingFactor.DstColor, BlendingFactor.SrcColor);
                                    break;
                            }
                        }

                        foreach (var inst in instances)
                        {
                            var m = inst.World;
                            if (_uPlaceModel >= 0)
                                _gl.UniformMatrix4(_uPlaceModel, 1, false, MemoryMarshal.CreateReadOnlySpan(ref m.M11, 16));
                            bool team = section.IsTeamColor && inst.OwnerId >= 0; // doodads stay gray
                            if (_uPlaceTeamColor >= 0) _gl.Uniform1(_uPlaceTeamColor, team ? 1 : 0);
                            if (team && _uPlaceOwnerColor >= 0)
                            {
                                var (r, g, b) = Wc3.Commands.PlayerColors.Color(inst.OwnerId);
                                _gl.Uniform3(_uPlaceOwnerColor, r / 255f, g / 255f, b / 255f);
                            }
                            // Unit and doodad creation numbers overlap, so the highlight
                            // match is kind-aware: units against the selected set, doodads
                            // against the single selected doodad.
                            bool highlit = inst.CreationNumber is int icn
                                && (inst.IsUnit ? selected.Contains(icn) : icn == selectedDoodad);
                            if (_uPlaceHighlight >= 0)
                                _gl.Uniform1(_uPlaceHighlight, highlit ? 1 : 0);
                            _gl.DrawElements(PrimitiveType.Triangles, (uint)section.IndexCount,
                                DrawElementsType.UnsignedInt, (void*)(section.IndexOffset * sizeof(uint)));
                        }

                        if (translucent)
                        {
                            _gl.Disable(EnableCap.Blend);
                            _gl.DepthMask(true);
                            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha); // sane default
                        }
                    }
                }
                _gl.BindVertexArray(0);
                _gl.ActiveTexture(TextureUnit.Texture0); // restore the terrain's unit
            }

            // Water: translucent sheet drawn after the opaque terrain and the placements
            // so it tints whatever sits below the surface. Depth-tested against the
            // terrain (ground rising above the level still occludes it) but not
            // depth-written (the ground under shallow water stays visible through the
            // blend). Blend and depth-mask state is restored right after the draw.
            if (_waterIndexCount > 0 && _waterProgram != 0)
            {
                _gl.UseProgram(_waterProgram);
                if (_uWaterMvp >= 0)
                    _gl.UniformMatrix4(_uWaterMvp, 1, false, MemoryMarshal.CreateReadOnlySpan(ref mvp.M11, 16));
                if (_uWaterColor >= 0)
                    _gl.Uniform4(_uWaterColor, WaterR, WaterG, WaterB, WaterA);
                _gl.Enable(EnableCap.Blend);
                _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                _gl.DepthMask(false);
                _gl.BindVertexArray(_waterVao);
                _gl.DrawElements(PrimitiveType.Triangles, (uint)_waterIndexCount, DrawElementsType.UnsignedInt, (void*)0);
                _gl.BindVertexArray(0);
                _gl.DepthMask(true);
                _gl.Disable(EnableCap.Blend);
            }
        }
        catch (Exception ex)
        {
            Log($"OnOpenGlRender failed: {ex.Message}");
        }
    }

    private unsafe void UploadMesh()
    {
        _meshDirty = false;
        _indexCount = 0;
        if (_gl is null || _verts is null || _indices is null) return;

        _gl.BindVertexArray(_vao);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.BufferData<float>(BufferTargetARB.ArrayBuffer,
            (nuint)(_verts.Length * sizeof(float)), _verts.AsSpan(), BufferUsageARB.StaticDraw);

        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        _gl.BufferData<uint>(BufferTargetARB.ElementArrayBuffer,
            (nuint)(_indices.Length * sizeof(uint)), _indices.AsSpan(), BufferUsageARB.StaticDraw);

        uint stride = (uint)(TerrainMesh.Stride * sizeof(float));
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, stride, (void*)(8 * sizeof(float)));
        _gl.EnableVertexAttribArray(3);

        _gl.BindVertexArray(0);
        _indexCount = _indices.Length;
    }

    /// <summary>Uploads the water surface mesh, mirroring <see cref="UploadMesh"/>.
    /// An empty mesh (map without water) leaves the index count at zero so the
    /// water pass skips drawing entirely.</summary>
    private unsafe void UploadWaterMesh()
    {
        _waterDirty = false;
        _waterIndexCount = 0;
        if (_gl is null || _waterVerts is null || _waterIndices is null || _waterIndices.Length == 0)
            return;

        _gl.BindVertexArray(_waterVao);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _waterVbo);
        _gl.BufferData<float>(BufferTargetARB.ArrayBuffer,
            (nuint)(_waterVerts.Length * sizeof(float)), _waterVerts.AsSpan(), BufferUsageARB.StaticDraw);

        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _waterEbo);
        _gl.BufferData<uint>(BufferTargetARB.ElementArrayBuffer,
            (nuint)(_waterIndices.Length * sizeof(uint)), _waterIndices.AsSpan(), BufferUsageARB.StaticDraw);

        uint stride = (uint)(WaterMesh.Stride * sizeof(float));
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        _gl.EnableVertexAttribArray(0);

        _gl.BindVertexArray(0);
        _waterIndexCount = _waterIndices.Length;
    }

    /// <summary>
    /// Brings the GPU in line with the current placement scene: uploads meshes for
    /// types not yet on the GPU (each unique model uploads exactly once), frees meshes
    /// no longer referenced, and regroups the per-instance transforms by mesh.
    /// </summary>
    private void SyncPlacementScene()
    {
        _sceneDirty = false;
        if (_gl is null) return;
        _instanceGroups.Clear();

        var scene = _scene;
        var live = new HashSet<string>();
        int uploaded = 0;
        if (scene is not null)
        {
            foreach (var mesh in scene.Meshes)
            {
                live.Add(mesh.Key);
                if (_gpuMeshes.ContainsKey(mesh.Key))
                    continue; // this unique model is already on the GPU
                _gpuMeshes[mesh.Key] = UploadPlacementMesh(mesh);
                uploaded++;
            }
            foreach (var inst in scene.Instances)
            {
                if (!_instanceGroups.TryGetValue(inst.MeshKey, out var list))
                    _instanceGroups[inst.MeshKey] = list = new List<PlacementInstance>();
                list.Add(inst); // whole instance: the draw loop needs owner + creation number
            }
        }

        // Free GPU meshes whose type vanished (map switch, undo of a type's last placement).
        List<string>? stale = null;
        foreach (var key in _gpuMeshes.Keys)
            if (!live.Contains(key))
                (stale ??= new List<string>()).Add(key);
        if (stale is not null)
            foreach (var key in stale)
            {
                DeletePlacementMesh(_gpuMeshes[key]);
                _gpuMeshes.Remove(key);
            }

        if (scene is not null && (uploaded > 0 || stale is not null))
            Log($"placement scene: {scene.Meshes.Count} unique meshes (+{uploaded}/-{stale?.Count ?? 0} on GPU), " +
                $"{scene.Instances.Count} instances ({scene.UnitCount} units, {scene.DoodadCount} doodads)");
    }

    private unsafe GpuMesh UploadPlacementMesh(PlacementMesh mesh)
    {
        uint vao = _gl!.GenVertexArray();
        uint vbo = _gl.GenBuffer();
        uint ebo = _gl.GenBuffer();

        _gl.BindVertexArray(vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        _gl.BufferData<float>(BufferTargetARB.ArrayBuffer,
            (nuint)(mesh.Vertices.Length * sizeof(float)), mesh.Vertices.AsSpan(), BufferUsageARB.StaticDraw);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);
        _gl.BufferData<uint>(BufferTargetARB.ElementArrayBuffer,
            (nuint)(mesh.Indices.Length * sizeof(uint)), mesh.Indices.AsSpan(), BufferUsageARB.StaticDraw);

        uint stride = (uint)(PlacementMesh.Stride * sizeof(float));
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, false, stride, (void*)(8 * sizeof(float)));
        _gl.EnableVertexAttribArray(3);

        _gl.BindVertexArray(0);

        // The mesh's decoded textures, one GL texture each (mipmapped; Repeat matches
        // the CPU renderer's `u -= floor(u)` wrap; rows upload as-is — WC3 v=0 is row 0).
        var textures = new uint[mesh.Textures.Count];
        for (int i = 0; i < textures.Length; i++)
        {
            var img = mesh.Textures[i];
            uint tex = _gl.GenTexture();
            _gl.ActiveTexture(TextureUnit.Texture1);
            _gl.BindTexture(TextureTarget.Texture2D, tex);
            _gl.TexImage2D<byte>(TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
                (uint)img.Width, (uint)img.Height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, img.Rgba.AsSpan());
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            ApplyAnisotropy(TextureTarget.Texture2D);
            _gl.GenerateMipmap(TextureTarget.Texture2D);
            _gl.BindTexture(TextureTarget.Texture2D, 0);
            textures[i] = tex;
        }
        _gl.ActiveTexture(TextureUnit.Texture0);

        return new GpuMesh(vao, vbo, ebo, textures, mesh.Sections);
    }

    private void DeletePlacementMesh(GpuMesh mesh)
    {
        if (_gl is null) return;
        if (mesh.Vbo != 0) _gl.DeleteBuffer(mesh.Vbo);
        if (mesh.Ebo != 0) _gl.DeleteBuffer(mesh.Ebo);
        if (mesh.Vao != 0) _gl.DeleteVertexArray(mesh.Vao);
        foreach (var tex in mesh.Textures)
            if (tex != 0) _gl.DeleteTexture(tex);
    }

    private void UploadTexArray()
    {
        _texDirty = false;
        if (_gl is null || _layerData is null || _layerCount <= 0) return;
        if (_texArray == 0) _texArray = _gl.GenTexture();
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2DArray, _texArray);
        _gl.TexImage3D<byte>(TextureTarget.Texture2DArray, 0, InternalFormat.Rgba8,
            (uint)_layerCell, (uint)_layerCell, (uint)_layerCount, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, _layerData.AsSpan());
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        ApplyAnisotropy(TextureTarget.Texture2DArray);
        _gl.GenerateMipmap(TextureTarget.Texture2DArray);
        _gl.BindTexture(TextureTarget.Texture2DArray, 0);
    }

    /// <summary>
    /// Sets GL_TEXTURE_MAX_ANISOTROPY (EXT_texture_filter_anisotropic / GL 4.6) on the
    /// currently bound texture to min(8, the implementation max). The max is queried once
    /// per context; when the extension is unsupported the query yields 0 (or throws) and
    /// this becomes a permanent no-op.
    /// </summary>
    private void ApplyAnisotropy(TextureTarget target)
    {
        if (_gl is null) return;
        if (_anisotropy < 0f)
        {
            try
            {
                _gl.GetFloat((GetPName)0x84FF, out float max); // GL_MAX_TEXTURE_MAX_ANISOTROPY_EXT
                _anisotropy = MathF.Min(8f, max);
            }
            catch { _anisotropy = 0f; }
        }
        if (_anisotropy < 1f) return;
        try { _gl.TexParameter(target, (TextureParameterName)0x84FE, _anisotropy); } catch { }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        try
        {
            if (_gl is null) return;
            if (_vbo != 0) _gl.DeleteBuffer(_vbo);
            if (_ebo != 0) _gl.DeleteBuffer(_ebo);
            if (_vao != 0) _gl.DeleteVertexArray(_vao);
            if (_waterVbo != 0) _gl.DeleteBuffer(_waterVbo);
            if (_waterEbo != 0) _gl.DeleteBuffer(_waterEbo);
            if (_waterVao != 0) _gl.DeleteVertexArray(_waterVao);
            if (_texArray != 0) _gl.DeleteTexture(_texArray);
            if (_program != 0) _gl.DeleteProgram(_program);
            if (_waterProgram != 0) _gl.DeleteProgram(_waterProgram);
            foreach (var gpuMesh in _gpuMeshes.Values)
                DeletePlacementMesh(gpuMesh);
            if (_placeProgram != 0) _gl.DeleteProgram(_placeProgram);
        }
        catch { /* never crash the UI on teardown */ }
        finally
        {
            _vbo = _ebo = _vao = _texArray = _program = 0;
            _waterVbo = _waterEbo = _waterVao = _waterProgram = 0;
            _waterIndexCount = 0;
            _placeProgram = 0;
            _gpuMeshes.Clear();     // GL names die with the context — never reuse them
            _instanceGroups.Clear();
            _anisotropy = -1f;      // re-query on the next context
            _initialized = false;
        }
    }

    private static uint CompileShader(GL gl, ShaderType type, string src)
    {
        uint sh = gl.CreateShader(type);
        gl.ShaderSource(sh, src);
        gl.CompileShader(sh);
        gl.GetShader(sh, ShaderParameterName.CompileStatus, out int ok);
        if (ok == 0)
            Log($"{type} compile failed: " + gl.GetShaderInfoLog(sh));
        return sh;
    }

    private static void Log(string msg)
    {
        try { File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] [TerrainGlView] {msg}{Environment.NewLine}"); }
        catch { /* diagnostics must never crash the UI */ }
    }
}
