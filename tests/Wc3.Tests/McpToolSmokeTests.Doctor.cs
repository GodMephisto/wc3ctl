// tests/Wc3.Tests/McpToolSmokeTests.Doctor.cs
// The doctor tools run an external program, which a CI machine does not have. With it installed
// each tool must answer on the fixture, and without it each must refuse and say how to install it,
// so the wiring is exercised either way and neither case passes vacuously.
using Wc3.Commands;

namespace Wc3.Tests;

public sealed partial class McpToolSmokeTests
{
    private static bool DoctorInstalled()
    {
        try { MapDoctorCommand.Locate(); return true; }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException or InvalidOperationException) { return false; }
    }

    private async Task Doctor(string tool, Dictionary<string, object?> args)
    {
        if (DoctorInstalled())
        {
            var r = await Call(tool, args);
            Assert.Equal(0, Prop(r, "exitCode").GetInt32());
        }
        else
        {
            Assert.Contains("WC3_DOCTOR", await CallError(tool, args));
        }
    }

    [Fact, Covers("doctor_check")]
    public Task Doctor_check_answers_or_names_the_install() => Doctor("doctor_check", Args(("map", Fixture)));

    [Fact, Covers("doctor_inspect")]
    public Task Doctor_inspect_answers_or_names_the_install() =>
        Doctor("doctor_inspect", Args(("map", Fixture), ("report", "files")));

    [Fact, Covers("doctor_compare")]
    public Task Doctor_compare_answers_or_names_the_install() =>
        Doctor("doctor_compare", Args(("map", Fixture), ("other", Fixture)));

    [Fact, Covers("doctor_fix")]
    public async Task Doctor_fix_never_writes_the_input()
    {
        // A blank map has nothing to repair, so the doctor answers "nothing" and no copy is written.
        var before = Sha(Fixture);
        var outPath = NewOut("doctor_fix");
        await Doctor("doctor_fix", Args(("map", Fixture), ("out_path", outPath)));
        Assert.Equal(before, Sha(Fixture));
        Assert.False(File.Exists(outPath));
    }
}
