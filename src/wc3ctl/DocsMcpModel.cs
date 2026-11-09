// src/wc3ctl/DocsMcpModel.cs
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Wc3.Commands;
using Wc3.Mcp;

namespace Wc3Ctl;

/// <summary>
/// The MCP tool surface as the generated reference needs it. The family comes from
/// <see cref="McpToolFamilies"/>, the same inventory the catalog resource serves, so the reference and
/// the catalog cannot name different families for one tool. The parameters come from the input schema
/// the tool publishes, in the order the schema declares them. The result shape comes from the return
/// type of the method behind the tool, because no tool publishes an output schema, and a method typed
/// <c>object</c> reports that its shape is chosen by the arguments rather than inventing one. The error
/// codes come from what the tool's own served text names, plus the two codes every call passes through.
///
/// This lives in the front end and not in Wc3.Commands, because it reads the MCP SDK and the server's
/// own types while the formatter there is free of both.
/// </summary>
internal static class DocsMcpModel
{
    /// <summary>The codes every served call can return, whatever the tool does. A call can be refused by
    /// the permission file's deny list, and any refusal or failure that reaches the call filter without
    /// its own code is reported as a command rejection.</summary>
    private static readonly HashSet<string> Universal =
        new(StringComparer.Ordinal) { "POLICY_DENIED", "COMMAND_REJECTED" };

    /// <summary>The most members one line names before it says how many it left out.</summary>
    private const int Listed = 12;

    /// <summary>The most enum names one result line lists.</summary>
    private const int EnumNames = 6;

    private const string AnyValue = "any JSON value";

    /// <summary>Tool name to the return type of the method that serves it.</summary>
    private static readonly IReadOnlyDictionary<string, Type> Results = BindResults();

    /// <summary>Every tool, sorted by name, ready for <see cref="DocsReference.RenderMcp"/>.</summary>
    public static IReadOnlyList<DocTool> Build() =>
        Wc3McpServer.CreateTools()
            .Select(t => t.ProtocolTool)
            .Select(t => new DocTool(
                t.Name,
                t.Description,
                McpToolFamilies.Of(t.Name),
                t.Annotations?.ReadOnlyHint,
                t.Annotations?.DestructiveHint,
                t.Annotations?.IdempotentHint,
                t.Annotations?.OpenWorldHint,
                ParametersOf(t.InputSchema),
                ShapeOf(Results.TryGetValue(t.Name, out Type? result) ? result : null),
                CodesOf(t.Description, t.InputSchema)))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>The two types that carry tool methods, scanned once so each entry can name its result.</summary>
    private static IReadOnlyDictionary<string, Type> BindResults()
    {
        var byName = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (Type host in new[] { typeof(Wc3Tools), typeof(Wc3Context) })
        {
            foreach (MethodInfo method in host.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                McpServerToolAttribute? attribute = method.GetCustomAttribute<McpServerToolAttribute>();
                if (attribute is null) continue;
                string name = string.IsNullOrEmpty(attribute.Name) ? method.Name : attribute.Name;
                byName[name] = method.ReturnType;
            }
        }
        return byName;
    }

    // ---- parameters ----

    /// <summary>Every property of the input schema, in the order it is declared, with its type and the
    /// default it declares. The schema's own type word is kept, so a union reads as the parts joined by
    /// a pipe rather than being flattened into one word.</summary>
    private static IReadOnlyList<DocParameter> ParametersOf(JsonElement schema)
    {
        var parameters = new List<DocParameter>();
        if (schema.ValueKind != JsonValueKind.Object
            || !schema.TryGetProperty("properties", out JsonElement properties)
            || properties.ValueKind != JsonValueKind.Object)
            return parameters;

        var required = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetProperty("required", out JsonElement listed) && listed.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in listed.EnumerateArray())
                if (entry.ValueKind == JsonValueKind.String && entry.GetString() is { } name)
                    required.Add(name);
        }

        foreach (JsonProperty property in properties.EnumerateObject())
        {
            parameters.Add(new DocParameter(
                property.Name,
                TypeWord(property.Value),
                required.Contains(property.Name),
                DefaultWord(property.Value)));
        }
        return parameters;
    }

    private static string TypeWord(JsonElement spec)
    {
        if (spec.ValueKind != JsonValueKind.Object || !spec.TryGetProperty("type", out JsonElement type))
            return "any";
        if (type.ValueKind == JsonValueKind.String) return type.GetString() ?? "any";
        if (type.ValueKind != JsonValueKind.Array) return "any";
        return string.Join("|", type.EnumerateArray()
            .Where(part => part.ValueKind == JsonValueKind.String)
            .Select(part => part.GetString()));
    }

    /// <summary>The default as the schema states it, in JSON, or null when it declares none. A declared
    /// null is kept, because the schema said so and the reference says so.</summary>
    private static string? DefaultWord(JsonElement spec) =>
        spec.ValueKind == JsonValueKind.Object && spec.TryGetProperty("default", out JsonElement value)
            ? value.GetRawText()
            : null;

    // ---- result shape ----

    /// <summary>What a caller gets back, said from the declared return type. A null type means the
    /// surface has no method for the tool, which is reported rather than guessed at.</summary>
    private static string ShapeOf(Type? type)
    {
        if (type is null) return AnyValue + ", the surface declares no type for this tool";
        type = Unwrap(type);
        if (type == typeof(void) || type == typeof(Task)) return "no result";

        string scalar = Scalar(type);
        if (scalar.Length > 0) return scalar;
        if (type == typeof(object))
            return AnyValue + ", the method is typed object so the shape is chosen by the arguments";
        if (type == typeof(JsonElement) || type == typeof(JsonDocument)) return AnyValue;
        if (type.IsEnum) return "a JSON string, one of " + Names(type);

        if (type.IsArray) return "a JSON array of " + Plural(ShapeOf(type.GetElementType()));
        if (type.IsGenericType)
        {
            Type[] arguments = type.GetGenericArguments();
            Type definition = type.GetGenericTypeDefinition();
            if (definition == typeof(CappedItems<>))
            {
                return "one JSON object with the fields cap, truncated, total and items, where items is "
                    + "an array of " + Plural(ShapeOf(arguments[0]));
            }
            if (typeof(System.Collections.IDictionary).IsAssignableFrom(type) && arguments.Length == 2)
                return "a JSON object keyed by name, each value " + ShapeOf(arguments[1]);
            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && arguments.Length == 1)
                return "a JSON array of " + Plural(ShapeOf(arguments[0]));
        }
        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type)) return "a JSON array";
        return Fields(type);
    }

    /// <summary>A record or class as one JSON object, naming its members by the name the serializer
    /// gives them, which is the camel case a caller reads rather than the property as declared.</summary>
    private static string Fields(Type type)
    {
        var names = new List<string>();
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0) continue;
            if (property.GetCustomAttribute<JsonIgnoreAttribute>() is not null) continue;
            names.Add(JsonName(property) + " (" + JsonTypeName(property.PropertyType) + ")");
        }
        if (names.Count == 0) return "one JSON object";
        string listed = string.Join(", ", names.Take(Listed));
        return "one JSON object with the fields " + listed
            + (names.Count > Listed ? ", and " + (names.Count - Listed) + " more" : "");
    }

    private static string JsonName(PropertyInfo property)
    {
        if (property.GetCustomAttribute<JsonPropertyNameAttribute>() is { } named) return named.Name;
        string? policy = Wc3McpServer.JsonOptions.PropertyNamingPolicy?.ConvertName(property.Name);
        return policy ?? property.Name;
    }

    private static string JsonTypeName(Type type)
    {
        type = Unwrap(type);
        string scalar = Scalar(type);
        if (scalar.Length > 0) return scalar["a JSON ".Length..];
        if (type.IsEnum) return "string";
        if (type == typeof(JsonElement) || type == typeof(JsonDocument) || type == typeof(object)) return "any";
        if (type.IsArray) return "array";
        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && type != typeof(string)) return "array";
        return "object";
    }

    /// <summary>The one line a JSON scalar type reads as, or an empty string when the type is not a
    /// scalar.</summary>
    private static string Scalar(Type type)
    {
        if (type == typeof(string) || type == typeof(DateTime) || type == typeof(DateTimeOffset)
            || type == typeof(Guid) || type == typeof(TimeSpan)) return "a JSON string";
        if (type == typeof(bool)) return "a JSON boolean";
        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)
            || type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte))
            return "a JSON integer";
        if (type == typeof(double) || type == typeof(float) || type == typeof(decimal)) return "a JSON number";
        return string.Empty;
    }

    private static string Plural(string shape) => shape switch
    {
        "a JSON string" => "strings",
        "a JSON boolean" => "booleans",
        "a JSON integer" => "integers",
        "a JSON number" => "numbers",
        _ when shape.StartsWith("one JSON object with the fields ", StringComparison.Ordinal) =>
            "objects with the fields " + shape["one JSON object with the fields ".Length..],
        _ => shape,
    };

    private static string Names(Type type)
    {
        string[] names = Enum.GetNames(type);
        string listed = string.Join(", ", names.Take(EnumNames));
        return names.Length > EnumNames ? listed + ", and " + (names.Length - EnumNames) + " more" : listed;
    }

    private static Type Unwrap(Type type)
    {
        while (true)
        {
            Type? nullable = Nullable.GetUnderlyingType(type);
            if (nullable is not null)
            {
                type = nullable;
                continue;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
            {
                type = type.GetGenericArguments()[0];
                continue;
            }
            return type;
        }
    }

    // ---- error codes ----

    /// <summary>The codes the tool's own served text names. That is its description and every parameter
    /// description, which is where a writing tool states the refusals its write gate raises. The two
    /// codes every call passes through are left out here because the render adds them to every entry.</summary>
    private static IReadOnlyList<string> CodesOf(string? description, JsonElement schema)
    {
        var text = new StringBuilder(description ?? string.Empty);
        if (schema.ValueKind == JsonValueKind.Object
            && schema.TryGetProperty("properties", out JsonElement properties)
            && properties.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in properties.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object) continue;
                if (property.Value.TryGetProperty("description", out JsonElement note)
                    && note.ValueKind == JsonValueKind.String)
                    text.Append(' ').Append(note.GetString());
            }
        }
        string served = text.ToString();
        return Wc3ErrorCodes.All
            .Where(code => !Universal.Contains(code.Code))
            .Where(code => Regex.IsMatch(served, "\\b" + Regex.Escape(code.Code) + "\\b"))
            .Select(code => code.Code)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();
    }
}
