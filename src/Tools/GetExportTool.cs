using System.ComponentModel;

using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;

using Microsoft.Extensions.Logging;

using ModelContextProtocol.Server;

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace CUE4Mcp.Tools;

[McpServerToolType]
public class GetExportTool
{
    private readonly IFileProvider _fileProvider;
    private readonly ILogger<GetExportTool> _logger;

    public GetExportTool(ILogger<GetExportTool> logger, IFileProvider fileProvider)
    {
        _fileProvider = fileProvider;
        _logger = logger;
    }

    [McpServerTool(Name = "cue4-get-export")]
    [Description("Get a full JSON representation of a specific export from a package file.")]
    public async Task<object> GetExport(
        [Description("Path to the file containing the export (e.g., 'FortniteGame/Content/Athena/Items/Cosmetics/Characters/CID_A_112'). Must use Linux-style path separators (/).")]
        string path,
        [Description("Index of the export to retrieve (0-based)")]
        int exportIndex)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return new { error = "Path parameter is required", code = -32602 };
            }

            if (exportIndex < 0)
            {
                return new { error = "Export index must be non-negative", code = -32602 };
            }

            _logger.LogInformation("Loading export {Index} from file: {Path}", exportIndex, path);

            // Try to load the package
            IPackage? package;
            try
            {
                package = _fileProvider.LoadPackage(path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load package: {Path}", path);
                return new { error = $"Failed to load package: {ex.Message}", code = -32002 };
            }

            if (package == null)
            {
                return new { error = $"Package not found: {path}", code = -32002 };
            }

            var exports = package.ExportsLazy;
        
            if (exportIndex >= exports.Length)
            {
                return new { error = $"Export index {exportIndex} is out of range. Package has {exports.Length} exports.", code = -32602 };
            }

            // Load the specific export
            UObject export;
            try
            {
                export = exports[exportIndex].Value;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load export {Index} from {Path}", exportIndex, path);
                return new { error = $"Failed to load export {exportIndex}: {ex.Message}", code = -32002 };
            }

            var serialized = JsonConvert.SerializeObject(export, new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() });

            _logger.LogInformation("Successfully loaded export {Index} ({Name}) from {Path}", exportIndex, export.Name, path);
            return serialized;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting export");
            return new { error = ex.Message, stackTrace = ex.StackTrace };
        }
    }
}
