// src/Wc3.Studio/Panels/TerrainGlView.cs
using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Silk.NET.Core.Contexts;
using Silk.NET.OpenGL;
using Wc3.Render;

namespace Wc3.Studio.Panels;

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

    // CPU-side terrain-type tile textures (RGBA layers), uploaded as a 2D array when dirty.
    private byte[]? _layerData;
    private int _layerCount;
    private int _layerCell = TerrainArtCatalog.Cell;
    private bool _texDirty;

    // Orbit camera (mirrors TerrainView's perspective params).
    private float _yaw = 45f, _pitch = 30f, _zoom = 1f;
    private Vector3 _center;
    private float _radius = 512f;

    /// <summary>Loads a map's terrain into the viewport (safe to call off the GL thread).</summary>
    public void SetMap(Wc3.Model.MapDocument? doc)
    {
        try
        {
            if (doc is null) { _verts = null; _indices = null; _indexCount = 0; return; }
            var mesh = TerrainMeshBuilder.Build(doc);
            _verts = mesh.Vertices;
            _indices = mesh.Indices;
            _center = mesh.Center;
            _radius = mesh.Radius;
            _meshDirty = true;

            _layerData = TerrainArtCatalog.BuildLayersForMap(doc, out _layerCount, out _layerCell);
            _texDirty = true;

            RequestNextFrameRendering();
        }
        catch (Exception ex)
        {
            Log($"SetMap failed: {ex.Message}");
            _verts = null; _indices = null; _indexCount = 0;
        }
    }

    /// <summary>Sets the orbit-camera angle to match the 2D perspective view.</summary>
    public void SetCamera(float yaw, float pitch, float zoom)
    {
        _yaw = yaw; _pitch = pitch; _zoom = zoom;
        RequestNextFrameRendering();
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
                "in vec3 vNormal;\n" +
                "in vec2 vUV;\n" +
                "flat in vec4 vLayers;\n" +
                "uniform vec3 uLight;\n" +
                "uniform sampler2DArray uTiles;\n" +
                "out vec4 fragColor;\n" +
                "void main(){\n" +
                "  vec4 c00 = texture(uTiles, vec3(vUV, vLayers.x));\n" +
                "  vec4 c10 = texture(uTiles, vec3(vUV, vLayers.y));\n" +
                "  vec4 c01 = texture(uTiles, vec3(vUV, vLayers.z));\n" +
                "  vec4 c11 = texture(uTiles, vec3(vUV, vLayers.w));\n" +
                "  vec3 tex = mix(mix(c00.rgb, c10.rgb, vUV.x), mix(c01.rgb, c11.rgb, vUV.x), vUV.y);\n" +
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
            _initialized = true;
            _meshDirty = _verts is not null;
            _texDirty = _layerData is not null;
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
            if (_texDirty) UploadTexArray();
            if (_indexCount == 0) return;

            // Orbit camera around the scene centre (matches RenderPerspectivePng).
            float yaw = _yaw * (MathF.PI / 180f);
            float pitch = Math.Clamp(_pitch, 2f, 89f) * (MathF.PI / 180f);
            var eyeDir = new Vector3(
                MathF.Cos(pitch) * MathF.Cos(yaw),
                MathF.Cos(pitch) * MathF.Sin(yaw),
                MathF.Sin(pitch));
            float dist = _radius / MathF.Max(_zoom, 0.05f) * 2.4f;
            var eye = _center + eyeDir * dist;

            var view = Matrix4x4.CreateLookAt(eye, _center, Vector3.UnitZ);
            float aspect = (float)pxW / pxH;
            float near = MathF.Max(dist * 0.02f, 1f);
            float far = dist * 4f + _radius * 2f;
            var proj = Matrix4x4.CreatePerspectiveFieldOfView(48f * MathF.PI / 180f, aspect, near, far);
            var mvp = view * proj;

            _gl.UseProgram(_program);
            if (_texArray != 0)
            {
                _gl.ActiveTexture(TextureUnit.Texture0);
                _gl.BindTexture(TextureTarget.Texture2DArray, _texArray);
                if (_uTiles >= 0) _gl.Uniform1(_uTiles, 0);
            }
            var light = Vector3.Normalize(eyeDir + new Vector3(-0.3f, -0.2f, 0.7f));
            if (_uLight >= 0) _gl.Uniform3(_uLight, light.X, light.Y, light.Z);
            if (_uMvp >= 0)
                _gl.UniformMatrix4(_uMvp, 1, false, MemoryMarshal.CreateReadOnlySpan(ref mvp.M11, 16));

            _gl.BindVertexArray(_vao);
            _gl.DrawElements(PrimitiveType.Triangles, (uint)_indexCount, DrawElementsType.UnsignedInt, (void*)0);
            _gl.BindVertexArray(0);
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
        _gl.GenerateMipmap(TextureTarget.Texture2DArray);
        _gl.BindTexture(TextureTarget.Texture2DArray, 0);
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        try
        {
            if (_gl is null) return;
            if (_vbo != 0) _gl.DeleteBuffer(_vbo);
            if (_ebo != 0) _gl.DeleteBuffer(_ebo);
            if (_vao != 0) _gl.DeleteVertexArray(_vao);
            if (_texArray != 0) _gl.DeleteTexture(_texArray);
            if (_program != 0) _gl.DeleteProgram(_program);
        }
        catch { /* never crash the UI on teardown */ }
        finally
        {
            _vbo = _ebo = _vao = _texArray = _program = 0;
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
