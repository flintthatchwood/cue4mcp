using System.Text.Json.Nodes;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace cue4mcp.FunctionalTests.McpTests;

[TestClass]
public sealed class ListPackagesToolTests
{
    private static string ServerPath => Path.Combine(AppContext.BaseDirectory, "cue4mcp");

    private async Task<McpClient> CreateClientAsync()
    {
        TestSettings settings = TestSettings.Instance;
        return await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = ServerPath,
            Arguments = [$"--FileProvider:Directory={settings.FileProvider.Directory}",
                         $"--FileProvider:Game={settings.FileProvider.Game}",
                         $"--FileProvider:SearchOption={settings.FileProvider.SearchOption}"]
        }));
    }

    [TestMethod]
    public async Task ListPackages_ViaStdio_ReturnsPackages()
    {
        await using McpClient client = await CreateClientAsync();

        CallToolResult result = await client.CallToolAsync("cue4-list-packages", new Dictionary<string, object?>
        {
            ["pattern"] = ".*Dinos/Achatina.*"
        });

        Assert.IsFalse(result.IsError ?? false);
        string text = result.Content.OfType<TextContentBlock>().First().Text;
        JsonNode json = JsonNode.Parse(text)!;
        Assert.IsTrue(json["success"]!.GetValue<bool>());
        Assert.IsGreaterThan(0, json["filteredPackageCount"]!.GetValue<int>());
    }

    [TestMethod]
    public async Task GetPackage_ViaStdio_ReturnsExports()
    {
        await using McpClient client = await CreateClientAsync();

        // First get a package name
        CallToolResult listResult = await client.CallToolAsync("cue4-list-packages", new Dictionary<string, object?>
        {
            ["pattern"] = ".*Dinos/Achatina/Achatina_Character_BP$"
        });

        JsonNode listJson = JsonNode.Parse(listResult.Content.OfType<TextContentBlock>().First().Text)!;
        string packageName = listJson["packages"]![0]!.GetValue<string>();

        // Now get the package
        CallToolResult result = await client.CallToolAsync("cue4-get-package", new Dictionary<string, object?>
        {
            ["packageName"] = packageName
        });

        Assert.IsFalse(result.IsError ?? false);
        string text = result.Content.OfType<TextContentBlock>().First().Text;
        JsonNode json = JsonNode.Parse(text)!;
        JsonNode? exportCount = json["ExportCount"] ?? json["exportCount"];
        Assert.IsNotNull(exportCount, $"Response missing ExportCount. Keys: {string.Join(", ", json.AsObject().Select(p => p.Key))}");
        Assert.IsGreaterThan(0, exportCount.GetValue<int>());
    }
}
