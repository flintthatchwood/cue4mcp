using System.ComponentModel;
using CUE4Mcp.Domain;
using CUE4Parse.UE4.Assets.Exports;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

using Newtonsoft.Json;
namespace CUE4Mcp.Tools;

[McpServerToolType]
public class GetExportTool
{
    private readonly FileService _fileService;
    private readonly ILogger<GetExportTool> _logger;

    public GetExportTool(ILogger<GetExportTool> logger, FileService fileService)
    {
        _fileService = fileService;
        _logger = logger;
    }

    [McpServerTool(Name = "cue4-get-export")]
    [Description("Get a full JSON representation of a specific export from a package.")]
    public async Task<object> GetExport(
        [Description("Package name using forward slashes (e.g., 'Game/Maps/TheIsland', 'Game/Characters/Hero'). Can also accept file paths with extensions.")]
        string packageName,
        [Description("Index of the export to retrieve (0-based)")]
        int exportIndex)
    {
        try
        {
            UObject export = _fileService.GetExport(packageName, exportIndex);
            return JsonConvert.SerializeObject(export, Json.DefaultSerializerSettings);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid argument");
            return new { error = ex.Message, code = -32602 };
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Export operation failed");
            return new { error = ex.Message, code = -32002 };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting export");
            return new { error = ex.Message, stackTrace = ex.StackTrace };
        }
    }
}
