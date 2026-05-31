using System.ComponentModel;

using CUE4Mcp.Domain;

using Microsoft.Extensions.Logging;

using ModelContextProtocol.Server;

namespace CUE4Mcp.Tools;

[McpServerToolType]
public class GetPackageTool
{
    private readonly FileService _fileService;
    private readonly ILogger<GetPackageTool> _logger;

    public GetPackageTool(ILogger<GetPackageTool> logger, FileService fileService)
    {
        _fileService = fileService;
        _logger = logger;
    }

    [McpServerTool(Name = "cue4-get-package")]
    [Description("Get detailed information about a package, including its metadata and exports list. Returns a JSON representation of the package.")]
    public async Task<object> GetPackage(
        [Description("Package name using forward slashes (e.g., 'Game/Maps/TheIsland', 'Game/Characters/Hero'). Can also accept file paths with extensions.")]
        string packageName)
    {
        try
        {
            var package = _fileService.GetPackage(packageName);

            // Build exports list with basic info (not full export data)
            var exportsList = new List<object?>();
            var exports = package.ExportsLazy;
            var index = 0;
            foreach(var lazyExport in exports)
            {
                try
                {
                    var export = lazyExport.Value;
                    if(export == null)
                    {
                        exportsList.Add(null);
                        continue;
                    }
                    exportsList.Add(new
                    {
                        export.Name,
                        export.ExportType,
                        Class = export.Class?.Name,
                        Super = export.Super?.Name,
                        Outer = export.Outer?.Name,
                        Template = export.Template?.Name,
                        Flags = export.Flags.ToString(),
                        PropertyCount = export.Properties?.Count ?? 0
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load export {Index} from {PackageName}", index, packageName);
                    exportsList.Add(new
                    {
                        error = ex.Message
                    });
                }
                index++;
            }

            return new
            {
                package.Name,
                ExportCount = exports.Length,
                Exports = exportsList
            };
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid argument");
            return new { error = ex.Message, code = -32602 };
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Package operation failed");
            return new { error = ex.Message, code = -32002 };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting package info");
            return new { error = ex.Message, stackTrace = ex.StackTrace };
        }
    }
}
