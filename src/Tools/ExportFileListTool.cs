using System.ComponentModel;

using CUE4Mcp.Domain;

using Microsoft.Extensions.Logging;

using ModelContextProtocol.Server;

namespace CUE4Mcp.Tools;

[McpServerToolType]
public class ExportFileListTool
{
    private readonly FileService _fileService;
    private readonly ILogger<ExportFileListTool> _logger;

    public ExportFileListTool(ILogger<ExportFileListTool> logger, FileService fileService)
    {
        _fileService = fileService;
        _logger = logger;
    }

    [McpServerTool(Name = "cue4-export-package-list")]
    [Description("Export a complete list of all packages to disk as JSON. Writes to OutputDirectory/all-packages.json with both package names and file paths.")]
    public async Task<object> ExportPackageList()
    {
        try
        {
            string outputPath = await _fileService.ExportPackageListToDiskAsync();

            // Count packages from the output to provide summary
            string[] allPackages = _fileService.ListPackages().ToArray();

            return new
            {
                success = true,
                filePath = outputPath,
                packageCount = allPackages.Length,
                message = $"Exported {allPackages.Length} packages to {outputPath}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting package list");
            return new { error = ex.Message, stackTrace = ex.StackTrace };
        }
    }
}
