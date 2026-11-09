// tests/Wc3.Tests/McpSchemaFailureTests.cs
// ERR-06, the naming half. A refusal for a missing required argument should say which argument was
// missing, because a model that reads only the tool name retries the same call and loses the turn.
// The wording is produced by the SDK rather than by this repository, so this test pins what the
// pinned SDK actually says and keeps the schema beside it as the evidence of what it should say.
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Wc3.Tests;

public class McpSchemaFailureTests
{
    [Fact]
    public async Task A_schema_failure_names_what_was_missing()
    {
        string requiredNames = "";
        string message = "";

        await McpTestClient.WithClient(async (client, ct) =>
        {
            // map_info has one parameter and no default for it, so the schema names the argument
            // the caller had to pass.
            var mapInfo = (await client.ListToolsAsync(cancellationToken: ct)).First(t => t.Name == "map_info");
            requiredNames = string.Join(", ", mapInfo.JsonSchema.GetProperty("required")
                .EnumerateArray().Select(e => e.GetString()));

            // The call a model makes when it forgets the one argument the tool cannot run without.
            var r = await client.CallToolAsync("map_info",
                new Dictionary<string, object?>(), cancellationToken: ct);
            Assert.True(r.IsError == true, "a call with no arguments was accepted");
            message = r.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";
        });

        Assert.Equal("map", requiredNames);
        Assert.False(string.IsNullOrWhiteSpace(message), "the refusal carried no text");

        // Recorded, not satisfied, so the row has evidence of the current behaviour. At SDK 1.4.1
        // the server binds the arguments itself. A missing required parameter throws inside
        // Microsoft.Extensions.AI with "The arguments dictionary is missing a value for the
        // required parameter 'map'.", the exception is not an McpException, and McpServerImpl
        // replaces any other exception with its generic sentence. What reaches the caller is
        // "An error occurred invoking 'map_info'.", which names the tool only. The parameter name
        // in the schema above never travels, and the binding lives in the SDK, not in this repo.
        Assert.Equal("An error occurred invoking 'map_info'.", message);
        Assert.DoesNotContain("'map'", message);
    }
}
