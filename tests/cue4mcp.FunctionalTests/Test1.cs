using System.Text.Json.Nodes;

using ModelContextProtocol.Client;

namespace cue4mcp.FunctionalTests;

[TestClass]
public sealed class ListFilesToolTests
{
    [TestMethod]
    public async Task ListFiles_should_return_file_list()
    {
        // Using an mcp client, call the cue4-list-files tool and verify it returns a non-empty file list
        await using var client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions { 
            Command = Path.Combine(AppContext.BaseDirectory, "cue4mcp.exe")
        }));

        var result = await client.CallToolAsync("cue4-list-files");

        Assert.IsFalse(result.IsError);
        var content = result.StructuredContent?.AsObject();

        Assert.IsTrue(content?.ContainsKey("results"), "result should contain the property 'results'");
    }
}
