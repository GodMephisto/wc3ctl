// tests/Wc3.Studio.Tests/CorpusAvaloniaFactAttribute.cs
using System;
using System.IO;
using Xunit;
using Xunit.Sdk;

namespace Wc3.Studio.Tests;

/// <summary>
/// An [AvaloniaFact] that skips when a corpus map or the game install is missing. AvaloniaFactAttribute
/// is sealed, so this derives from FactAttribute and names Avalonia's own discoverer, which runs the test
/// on the headless UI thread exactly as [AvaloniaFact] does. The skip is decided per test when the
/// attribute is constructed at discovery, so a missing map is reported as Skipped with its path and never
/// turns into a silent pass.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
[XunitTestCaseDiscoverer("Avalonia.Headless.XUnit.AvaloniaUIFactDiscoverer", "Avalonia.Headless.XUnit")]
public sealed class CorpusAvaloniaFactAttribute : FactAttribute
{
    /// <param name="corpusMap">Corpus map file name, resolved through TestCorpus.Map.</param>
    /// <param name="needsGameData">Also skip when no Warcraft III install is found.</param>
    public CorpusAvaloniaFactAttribute(string corpusMap, bool needsGameData = false)
        : this(new[] { corpusMap }, needsGameData)
    {
    }

    /// <param name="corpusMaps">Corpus map file names, every one of which must exist.</param>
    public CorpusAvaloniaFactAttribute(params string[] corpusMaps)
        : this(corpusMaps, false)
    {
    }

    private CorpusAvaloniaFactAttribute(string[] corpusMaps, bool needsGameData)
    {
        foreach (var map in corpusMaps)
        {
            var path = Wc3.Tests.TestCorpus.Map(map);
            if (!File.Exists(path))
            {
                Skip = $"Corpus map missing {path}";
                return;
            }
        }

        if (needsGameData && Wc3.GameData.GameInstall.Locate() is null)
            Skip = "Warcraft III install not found";
    }
}
