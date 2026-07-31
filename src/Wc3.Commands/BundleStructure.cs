using System;
using System.Collections.Generic;
using System.Linq;

namespace Wc3.Commands;

/// <summary>
/// The one rule that separates a hero's real dependencies from the script closure
/// over-carry, shared by every front end so they cannot drift. An edge is a real object
/// reference when its Via is anything other than ScriptClosureVia. Objects the root reaches
/// only through that synthetic seed edge are carried, and belong in their own group, never
/// under the hero.
/// </summary>
public static class BundleStructure
{
    /// <summary>The Via that BundleCommand stamps on the synthetic root to object edge it adds
    /// for every object the trigger script drags in. Any other Via is a genuine field code.</summary>
    public const string ScriptClosureVia = "script closure";

    /// <summary>Real object to object references, the referencing rawcode to its distinct
    /// referenced rawcodes, dropping the script closure seed edges and any endpoint that is
    /// not an object in this bundle.</summary>
    public static Dictionary<string, List<string>> RealAdjacency(UnitBundle bundle)
    {
        var codes = bundle.Objects.Select(o => o.Rawcode).ToHashSet(StringComparer.Ordinal);
        return bundle.Edges
            .Where(e => e.Via != ScriptClosureVia && codes.Contains(e.From) && codes.Contains(e.To))
            .GroupBy(e => e.From, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.To).Distinct().ToList(),
                StringComparer.Ordinal);
    }

    /// <summary>Real object to object edges grouped by the referencing rawcode, keeping each
    /// edge so a caller can show the field code it came through. Same rule as RealAdjacency.</summary>
    public static Dictionary<string, List<BundleEdge>> RealChildEdges(UnitBundle bundle)
    {
        var codes = bundle.Objects.Select(o => o.Rawcode).ToHashSet(StringComparer.Ordinal);
        return bundle.Edges
            .Where(e => e.Via != ScriptClosureVia && codes.Contains(e.From) && codes.Contains(e.To))
            .GroupBy(e => e.From, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
    }

    /// <summary>Rawcodes the root reaches only through the script closure seed edge, never
    /// through a real reference. These are the deliberate over-carry, other heroes' kits and
    /// the like, and are shown in their own group rather than under the hero.</summary>
    public static HashSet<string> CarriedByScriptClosure(UnitBundle bundle)
    {
        var adjacency = RealAdjacency(bundle);
        var reachable = new HashSet<string>(StringComparer.Ordinal) { bundle.RootRawcode };
        var queue = new Queue<string>();
        queue.Enqueue(bundle.RootRawcode);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!adjacency.TryGetValue(current, out var targets))
                continue;
            foreach (var to in targets)
                if (reachable.Add(to))
                    queue.Enqueue(to);
        }
        return bundle.Objects
            .Where(o => o.Rawcode != bundle.RootRawcode && !reachable.Contains(o.Rawcode))
            .Select(o => o.Rawcode)
            .ToHashSet(StringComparer.Ordinal);
    }
}
