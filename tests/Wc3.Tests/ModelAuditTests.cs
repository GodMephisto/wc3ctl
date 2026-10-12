// tests/Wc3.Tests/ModelAuditTests.cs
using System.Text;
using Wc3.Commands;

namespace Wc3.Tests;

public sealed class ModelAuditTests
{
    private static string Mdl(int textureCount, string vertices)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Version { FormatVersion 800, }");
        sb.AppendLine($"Textures {textureCount} {{");
        for (int i = 0; i < textureCount; i++)
            sb.AppendLine("  Bitmap { Image \"Textures\\Test.blp\", },");
        sb.AppendLine("}");
        sb.AppendLine("Materials 1 { Material { Layer { FilterMode None, static TextureID 0, }, }, }");
        sb.AppendLine($$"""
            Geoset {
                Vertices 3 { {{vertices}}, },
                Normals 3 { { 0, 0, 1 }, { 0, 0, 1 }, { 0, 0, 1 }, },
                TVertices 3 { { 0, 0 }, { 1, 0 }, { 0, 1 }, },
                Faces 1 3 { Triangles { { 0, 1, 2 }, }, },
                MaterialID 0,
            }
            """);
        return sb.ToString();
    }

    private static ModelAuditResult Audit(string mdl)
        => ModelAuditCommand.Execute(Encoding.UTF8.GetBytes(mdl), "test.mdl");

    [Fact]
    public void A_clean_model_reports_no_issues()
    {
        var r = Audit(Mdl(1, "{ 0, 0, 0 }, { 1, 0, 0 }, { 0, 1, 0 }"));
        Assert.Equal("mdl", r.Format);
        Assert.Equal(1, r.Geosets);
        Assert.Equal(3, r.Vertices);
        Assert.Equal(1, r.Faces);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void A_duplicate_texture_is_reported()
    {
        var r = Audit(Mdl(2, "{ 0, 0, 0 }, { 1, 0, 0 }, { 0, 1, 0 }"));
        Assert.Contains(r.Issues, i => i.Kind == ModelIssueKind.DuplicateTexture);
    }

    [Fact]
    public void A_degenerate_triangle_is_reported()
    {
        var r = Audit(Mdl(1, "{ 0, 0, 0 }, { 1, 0, 0 }, { 2, 0, 0 }"));
        Assert.Contains(r.Issues, i => i.Kind == ModelIssueKind.ZeroAreaFace);
    }

    [Fact]
    public void Unparseable_bytes_report_a_diagnostic_not_an_issue()
    {
        var r = ModelAuditCommand.Execute(new byte[] { 1, 2, 3, 4 }, "broken.mdx");
        Assert.Empty(r.Issues);
        Assert.NotEmpty(r.Diagnostics);
    }
}
