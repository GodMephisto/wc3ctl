// src/Wc3.Commands/ObjectSetCommand.cs
using Wc3.Model;

namespace Wc3.Commands;

public static class ObjectSetCommand
{
    public sealed record ObjectSetResult(bool Ok, string Message);

    /// <summary>Sets a field on a map object (write-back arrives in a later slice).</summary>
    public static ObjectSetResult Execute(MapDocument doc, string rawcode, string field, string value) =>
        new(false, "object set not implemented yet");
}
