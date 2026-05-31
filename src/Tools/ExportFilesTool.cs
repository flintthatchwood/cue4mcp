using System.ComponentModel;

using CUE4Mcp.Domain;

using Microsoft.Extensions.Logging;

using ModelContextProtocol.Server;

namespace CUE4Mcp.Tools;

[McpServerToolType]
public class ExportFilesTool
{
    private readonly FileService _fileService;
    private readonly ILogger<ExportFilesTool> _logger;

    public ExportFilesTool(ILogger<ExportFilesTool> logger, FileService fileService)
    {
        _fileService = fileService;
        _logger = logger;
    }

    [McpServerTool(Name = "cue4-export-packages")]
    [Description("Export multiple packages including all exports and their properties to disk as JSON. Writes to OutputDirectory/{packageName}.json.")]
    public async Task<object> ExportPackages(
        [Description("Regex pattern to match package names (e.g., '.*Maps.*', 'Game/Characters/.*'). Matches against package names, not file paths.")]
        string pattern)
    {
        try
        {
            var exportedFiles = await _fileService.ExportPackagesToDiskAsync(pattern);

            return new
            {
                success = true,
                message = $"Exported {exportedFiles.Count} packages",
                filePaths = exportedFiles.Count < 10 ? exportedFiles : null,
            };
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid argument");
            return new { error = ex.Message, code = -32602 };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting packages");
            return new { error = ex.Message, stackTrace = ex.StackTrace };
        }
    }
}
