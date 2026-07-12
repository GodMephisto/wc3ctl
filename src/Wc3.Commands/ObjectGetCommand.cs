using Wc3.Model;

namespace Wc3.Commands;

public static class ObjectGetCommand
{
    // Full object-data indexing is a later slice; this returns Found=false for now
    // but establishes the command signature and result shape.
    public static ObjectGetResult Execute(MapDocument doc, string rawcode, string? field) =>
        new(rawcode, Found: false, Fields: new Dictionary<string, string>());
}
