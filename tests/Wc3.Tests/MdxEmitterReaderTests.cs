// tests/Wc3.Tests/MdxEmitterReaderTests.cs
using Wc3.Modeling;

namespace Wc3.Tests;

public sealed class MdxEmitterReaderTests
{
    [Fact]
    public void Reads_the_node_id_and_absent_texture_of_a_prem()
    {
        var bytes = SyntheticModel.BuildMdx(extraChunks: new[]
        {
            ("PREM", SyntheticModel.ParticleEmitter1Record(objectId: 3)),
        });
        var emitter = Assert.Single(MdxEmitterReader.Read(MdxDocument.Parse(bytes)));
        Assert.Equal("PREM", emitter.Tag);
        Assert.Equal(3, emitter.NodeId);
        Assert.Null(emitter.TextureId);
    }

    [Fact]
    public void Reads_the_node_id_and_texture_id_of_a_pre2()
    {
        var bytes = SyntheticModel.BuildMdx(extraChunks: new[]
        {
            ("PRE2", SyntheticModel.ParticleEmitter2Record(objectId: 5, textureId: 1)),
        });
        var emitter = Assert.Single(MdxEmitterReader.Read(MdxDocument.Parse(bytes)));
        Assert.Equal("PRE2", emitter.Tag);
        Assert.Equal(5, emitter.NodeId);
        Assert.Equal(1, emitter.TextureId);
    }

    [Fact]
    public void Reads_every_record_in_a_chunk()
    {
        var payload = SyntheticModel.ParticleEmitter2Record(objectId: 5, textureId: 0)
            .Concat(SyntheticModel.ParticleEmitter2Record(objectId: 6, textureId: 2))
            .ToArray();
        var doc = MdxDocument.Parse(SyntheticModel.BuildMdx(extraChunks: new[] { ("PRE2", payload) }));
        var emitters = MdxEmitterReader.Read(doc);
        Assert.Equal(2, emitters.Count);
        Assert.Equal(new[] { 5, 6 }, emitters.Select(e => e.NodeId));
        Assert.Equal(new int?[] { 0, 2 }, emitters.Select(e => e.TextureId));
    }

    [Fact]
    public void An_empty_effect_chunk_yields_no_emitters()
    {
        var doc = MdxDocument.Parse(SyntheticModel.BuildMdx(extraChunks: new[] { ("PREM", new byte[8]) }));
        Assert.Empty(MdxEmitterReader.Read(doc));
    }

    [Fact]
    public void Node_count_is_taken_from_the_pivt_chunk()
    {
        var withPivots = MdxDocument.Parse(
            SyntheticModel.BuildMdx(extraChunks: new[] { ("PIVT", SyntheticModel.Pivots(4)) }));
        Assert.Equal(4, withPivots.NodeCount());
        Assert.Equal(0, MdxDocument.Parse(SyntheticModel.BuildMdx()).NodeCount());
    }
}
