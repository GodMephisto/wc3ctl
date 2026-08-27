// tests/Wc3.Tests/HeroSystemFallbackTests.cs
using Wc3.Commands;

namespace Wc3.Tests;

/// <summary>
/// A per-hero table that ends in a default already answers for a hero with no branch, so telling
/// someone to add one sends them to change something that is already correct. GGGA's skin-origin
/// table is exactly that case: it returns 0 for anything unlisted, and a newly installed hero is
/// not a skin of another hero, so having no entry is the right state.
/// </summary>
public class HeroSystemFallbackTests
{
    private static readonly string[] Roster =
        { "H001", "H002", "H004", "H005", "H006", "H007", "H008", "H009", "H00A", "H00B" };

    private static string Ladder(string body) =>
        "function TheTable takes integer heroCode returns integer\n" + body + "endfunction\n";

    private static string Branches() =>
        string.Concat(Roster.Select((c, i) =>
            (i == 0 ? "    if " : "    elseif ") + $"heroCode == '{c}' then\n        return {i + 1}\n"));

    [Fact]
    public void A_ladder_that_ends_in_a_default_is_reported_as_having_one()
    {
        var script = Ladder(Branches() + "    endif\n    return 0\n");

        var hit = HeroSystemAudit.Run(script, Roster, "H003").Single(s => s.Function == "TheTable");

        Assert.Equal(Roster.Length, hit.HeroCount);
        Assert.True(hit.HasFallback,
            "a return reached after the last endif is a default and must be reported as one");
    }

    [Fact]
    public void A_ladder_with_no_default_is_reported_as_a_real_gap()
    {
        var script = Ladder(Branches() + "    endif\n");

        var hit = HeroSystemAudit.Run(script, Roster, "H003").Single(s => s.Function == "TheTable");

        Assert.False(hit.HasFallback,
            "nothing follows the ladder, so a hero with no branch genuinely gets no answer");
    }

    [Fact]
    public void An_else_arm_counts_as_the_default()
    {
        var script = Ladder(Branches() + "    else\n        return 0\n    endif\n");

        Assert.True(HeroSystemAudit.Run(script, Roster, "H003")
            .Single(s => s.Function == "TheTable").HasFallback);
    }

    [Fact]
    public void A_table_the_hero_is_already_in_is_not_reported_at_all()
    {
        var script = Ladder(Branches() + "    elseif heroCode == 'H003' then\n        return 99\n    endif\n");

        Assert.DoesNotContain(HeroSystemAudit.Run(script, Roster, "H003"),
            s => s.Function == "TheTable");
    }
}
