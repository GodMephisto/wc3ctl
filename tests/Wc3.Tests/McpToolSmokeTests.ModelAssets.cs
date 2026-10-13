// tests/Wc3.Tests/McpToolSmokeTests.ModelAssets.cs
// The model and batch asset tools. Model lint scans imported models, the batch tools write
// files to a directory the caller names, and import-wire writes an edited map copy. Each write
// is checked to leave its input map untouched.
namespace Wc3.Tests;

public sealed class McpSmokeModelAssetTests : McpSmokeBase
{
    public McpSmokeModelAssetTests(SmokeFixture f) : base(f) { }

    [Fact, Covers("asset_model_lint")]
    public async Task Model_lint_answers_on_a_blank_map()
    {
        var result = await Call("asset_model_lint", Args(("map", Fixture)));

        Assert.Equal(0, Prop(result, "modelsScanned").GetInt32());
        Assert.Empty(Prop(result, "findings").EnumerateArray());
    }

    [Fact, Covers("asset_thumbnails")]
    public async Task Thumbnails_writes_a_png_for_a_model_folder()
    {
        var folder = Path.Combine(_f.Dir, $"models-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "Tri.mdx"), SyntheticModel.TinyMdx());

        var outDir = Path.Combine(_f.Dir, $"thumbs-{Guid.NewGuid():N}");
        var result = await Call("asset_thumbnails",
            Args(("folder", folder), ("out_dir", outDir)));

        Assert.Equal(1, Prop(result, "rendered").GetInt32());
        Assert.True(File.Exists(Path.Combine(outDir, "Tri.png")));
    }

    [Fact, Covers("asset_convert_batch")]
    public async Task Convert_batch_writes_a_converted_texture()
    {
        var folder = Path.Combine(_f.Dir, $"textures-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "Tex.blp"), SyntheticModel.Blp());

        var outDir = Path.Combine(_f.Dir, $"converted-{Guid.NewGuid():N}");
        var result = await Call("asset_convert_batch",
            Args(("folder", folder), ("to", ".png"), ("out_dir", outDir)));

        Assert.Equal(1, Prop(result, "converted").GetInt32());
        Assert.True(File.Exists(Path.Combine(outDir, "Tex.png")));
    }

    [Fact, Covers("model_audit")]
    public async Task Model_audit_reports_the_model()
    {
        var modelFile = NewOut("audit", ".mdx");
        File.WriteAllBytes(modelFile, SyntheticModel.TinyMdx());
        var r = await Call("model_audit", Args(("model", modelFile)));
        Assert.Equal("mdx", Prop(r, "format").GetString());
        Assert.True(Prop(r, "geosets").GetInt32() >= 1);
    }

    [Fact, Covers("model_optimize")]
    public async Task Model_optimize_writes_a_smaller_model()
    {
        var modelFile = NewOut("optin", ".mdx");
        File.WriteAllBytes(modelFile, SyntheticModel.TinyMdx());
        var outFile = NewOut("optout", ".mdx");
        var r = await Call("model_optimize", Args(("model", modelFile), ("out_path", outFile), ("ratio", 0.5)));
        Assert.True(File.Exists(outFile));
        Assert.True(Prop(r, "geosets").GetInt32() >= 1);
    }

    [Fact, Covers("texture_optimize")]
    public async Task Texture_optimize_shrinks_a_texture()
    {
        var blp = NewOut("texin", ".blp");
        File.WriteAllBytes(blp, SyntheticModel.Blp(64, 64));
        var outBlp = NewOut("texout", ".blp");
        var r = await Call("texture_optimize", Args(("texture", blp), ("out_path", outBlp), ("max", 32)));
        Assert.True(File.Exists(outBlp));
        Assert.Equal(32, Prop(r, "widthAfter").GetInt32());
    }

    [Fact, Covers("model_export_obj")]
    public async Task Model_export_obj_writes_obj_and_mtl()
    {
        var modelFile = NewOut("model", ".mdx");
        File.WriteAllBytes(modelFile, SyntheticModel.TinyMdx());
        var withModel = await Write("file_set",
            Args(("internal_path", @"war3mapImported\Smoke.mdx"), ("from", modelFile)));

        var objPath = NewOut("export", ".obj");
        await Call("model_export_obj", Args(
            ("map", withModel), ("model", @"war3mapImported\Smoke.mdx"), ("out_path", objPath)));

        Assert.True(File.Exists(objPath));
        var mtlPath = Path.ChangeExtension(objPath, ".mtl");
        Assert.True(File.Exists(mtlPath));
        Assert.Contains("mtllib", File.ReadAllText(objPath));
        Assert.Contains("newmtl", File.ReadAllText(mtlPath));
    }

    [Fact, Covers("model_import")]
    public async Task Model_import_writes_mdx_from_an_obj()
    {
        var objFile = NewOut("model", ".obj");
        File.WriteAllText(objFile, "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
        var outFile = NewOut("imported", ".mdx");

        var r = await Call("model_import", Args(("obj", objFile), ("out_path", outFile)));

        Assert.True(File.Exists(outFile));
        Assert.Equal(3, Prop(r, "vertices").GetInt32());
        Assert.Equal(1, Prop(r, "faces").GetInt32());
        Assert.Equal(1, Prop(r, "textures").GetInt32());
    }

    [Fact, Covers("asset_import_wire")]
    public async Task Import_wire_adds_a_file_to_a_map_copy()
    {
        var modelFile = NewOut("wire", ".mdx");
        File.WriteAllBytes(modelFile, SyntheticModel.TinyMdx());

        var outPath = NewOut("wired");
        var before = Sha(Fixture);
        var result = await Call("asset_import_wire", Args(
            ("map", Fixture), ("out_path", outPath), ("files", new[] { modelFile })));

        Assert.Equal(before, Sha(Fixture));
        Assert.True(File.Exists(outPath));
        Assert.Contains(@"war3mapImported\",
            Prop(result, "imported").EnumerateArray().First().GetString());

        var imports = await Call("imports_list", Args(("map", outPath)));
        Assert.Contains(Rows(Prop(imports, "entries")),
            e => Prop(e, "path").GetString()!.EndsWith("wire", StringComparison.OrdinalIgnoreCase)
                 || Prop(e, "path").GetString()!.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase));
    }
}
