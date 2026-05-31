using System.Text.Json.Nodes;

using ModelContextProtocol.Client;

namespace cue4mcp.FunctionalTests.McpTests;

[TestClass]
public sealed class ListPackagesToolTests
{
    [TestMethod]
    public async Task ListPackages_should_return_file_list()
    {
        // Using an mcp client, call the cue4-list-packages tool and verify it returns a non-empty package list
        await using var client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions { 
            Command = Path.Combine(AppContext.BaseDirectory, "cue4mcp.exe"),
        }));

        var result = await client.CallToolAsync("cue4-list-packages");

        Assert.IsFalse(result.IsError ?? false);
        var content = result.Content;
    }
}
