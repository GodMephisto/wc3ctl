// tests/Wc3.Tests/LibrarySaveTests.cs
using Wc3.Commands;
using Wc3.Model;
using Xunit;
using Xunit.Abstractions;

namespace Wc3.Tests;

/// <summary>
/// Whether the maps in the user's own Maps folder can actually be saved, byte-faithfully.
///
/// Nothing had ever swept the library for this, and 7 of 34 could not be saved at all, which for a
/// tool whose whole promise is byte-faithful editing is the most serious failure there is. Three
/// distinct causes, one of them ours.
/// </summary>
public class LibrarySaveTests
{
    private readonly ITestOutputHelper _out;
    public LibrarySaveTests(ITestOutputHelper output) => _out = output;

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Warcraft III", "Maps", "Download");

    /// <summary>
    /// The four that failed with "Unable to re-encode the mpq file, because its stream cannot be
    /// read". Their archives use a block-size shift of 13 or 14, meaning 4 MB and 8 MB sectors,
    /// while the rebuild was creating a 4 KB one. That forced every file to be re-encoded, and the
    /// protected ones, whose encryption keys are unrecoverable, could not be.
    /// </summary>
    [Theory]
    [InlineData("Angel-samurai-Z-v332A.w3x")]
    [InlineData("Angel-samurai-Z-v338.w3x")]
    [InlineData("Angel_samurai_338A_Optimize.w3x")]
    [InlineData("LODSuperM2.8B1.w3x")]
    [Trait("Category", "Corpus")]
    public void A_map_with_an_unusual_sector_size_saves_byte_faithfully(string mapName)
    {
        var path = Path.Combine(Dir, mapName);
        if (!File.Exists(path)) { _out.WriteLine($"{mapName} absent, skipped"); return; }

        var doc = MapDocument.Load(path);
        var result = RoundtripCommand.Execute(doc);

        _out.WriteLine($"{mapName}: {doc.Files.Count:N0} entries, "
                     + $"{result.Mismatches.Count} mismatch(es)");
        foreach (var m in result.Mismatches.Take(5)) _out.WriteLine($"   {m}");

        Assert.True(result.Faithful,
            $"{mapName} does not round-trip: {string.Join(", ", result.Mismatches.Take(5))}");
    }

    /// <summary>
    /// The one whose save was never broken. It holds 14 duplicated internal names, and the
    /// COMPARISON threw on them, so a map that writes all 100,788,325 of its bytes correctly was
    /// reported as a round-trip failure.
    /// </summary>
    [Fact]
    [Trait("Category", "Corpus")]
    public void A_map_with_duplicated_internal_names_compares_rather_than_throwing()
    {
        var path = Path.Combine(Dir, "NCD S1 ENGv1b.w3x");
        if (!File.Exists(path)) { _out.WriteLine("absent, skipped"); return; }

        var doc = MapDocument.Load(path);
        var dupes = doc.Files.Where(f => f.FileName is not null)
            .GroupBy(f => f.FileName!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).ToList();
        _out.WriteLine($"{dupes.Count} duplicated name(s), e.g. "
                     + string.Join(", ", dupes.Take(3).Select(g => $"{g.Key} x{g.Count()}")));
        Assert.NotEmpty(dupes);   // the whole point of this map as a fixture

        var result = RoundtripCommand.Execute(doc);   // must not throw
        _out.WriteLine($"round-trip ok={result.Faithful}, {result.Mismatches.Count} mismatch(es)");
        Assert.True(result.Faithful,
            $"mismatches: {string.Join(", ", result.Mismatches.Take(5))}");
    }

    /// <summary>
    /// The two that still cannot be saved, with 65,534 of their 65,536 hash slots occupied. That
    /// is map protection rather than a real archive shape, and the fix would have to be in the MPQ
    /// library. What is pinned here is that the refusal EXPLAINS itself, since the failure used to
    /// surface as a raw "Stream length must be non-negative" with nothing a reader could act on.
    /// </summary>
    [Theory]
    [InlineData("ORDR_S2_2.305[R]_english.w3x")]
    [InlineData("PumpkinTD_v2.3b.w3x")]
    [Trait("Category", "Corpus")]
    public void A_map_that_cannot_be_rebuilt_says_why(string mapName)
    {
        var path = Path.Combine(Dir, mapName);
        if (!File.Exists(path)) { _out.WriteLine($"{mapName} absent, skipped"); return; }

        var doc = MapDocument.Load(path);
        // Reading it still works. That is worth pinning too, because "cannot save" must not
        // quietly become "cannot open".
        Assert.NotEmpty(doc.Files);
        Assert.NotNull(doc.GetFile("war3map.w3i"));

        var ex = Assert.Throws<NotSupportedException>(() => doc.SaveToBytes());
        _out.WriteLine(ex.Message);

        Assert.Contains("cannot be rebuilt", ex.Message);
        Assert.Contains("protection", ex.Message);
        Assert.Contains("65,536 entries", ex.Message);
        Assert.NotNull(ex.InnerException);   // the underlying cause stays reachable
    }
}
