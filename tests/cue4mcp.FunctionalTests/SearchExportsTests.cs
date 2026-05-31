using CUE4Mcp.Domain;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

namespace cue4mcp.FunctionalTests;

[TestClass]
public class SearchExportsTests
{
    [TestMethod]
    public async Task TestMethod1()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddSimpleConsole(x => x.SingleLine = true));
        var logger = loggerFactory.CreateLogger<FileService>();

        var options = Options.Create(new FileServiceOptions {
            Directory = "D:/repos/ark-runner/live/game-2430930/ShooterGame/Content/Paks",
            OutputDirectory = "D:/repos/ark/Assets",
            Game = "ARKSurvivalAscended",
            SearchOption = "AllDirectories"
        });

        var fileService = new FileService(logger, options);
        await fileService.InitializeAsync();

        var result = await fileService.ExportPackagesToDiskAsync(".");
    }
}