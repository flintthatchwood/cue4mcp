using System.ComponentModel;

using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets;

using Microsoft.Extensions.Logging;

using ModelContextProtocol.Server;

namespace CUE4Mcp.Tools;

[McpServerToolType]
public class GetFileTool
{
    private readonly IFileProvider _fileProvider;
    private readonly ILogger<GetFileTool> _logger;

    public GetFileTool(ILogger<GetFileTool> logger, IFileProvider fileProvider)
    {
        _fileProvider = fileProvider;
        _logger = logger;
    }

    [McpServerTool(Name = "cue4-get-file")]
    [Description("Get detailed information about a file, including its metadata and exports list. Returns a JSON representation of the package.")]
    public async Task<object> GetFile(
        [Description("Path to the file to load (e.g., 'FortniteGame/Content/Athena/Items/Cosmetics/Characters/CID_A_112'). Must use Linux-style path separators (/).")]
        string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return new { error = "Path parameter is required", code = -32602 };
            }

            _logger.LogInformation("Loading file: {Path}", path);

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

            // Build exports list with basic info (not full export data)
            var exportsList = new List<object>();
            var exports = package.ExportsLazy;
        
            for (int i = 0; i < exports.Length; i++)
            {
                try
                {
                    var export = exports[i].Value;
                    exportsList.Add(new
                    {
                        index = i,
                        name = export.Name,
                        exportType = export.ExportType,
                        className = export.Class?.Name,
                        flags = export.Flags.ToString(),
                        // Include a few key properties if available
                        propertyCount = export.Properties?.Count ?? 0
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load export {Index} from {Path}", i, path);
                    exportsList.Add(new
                    {
                        index = i,
                        name = "(error loading export)",
                        error = ex.Message
                    });
                }
            }

            // Build package info
            var packageInfo = new
            {
                success = true,
                name = package.Name,
                summary = new
                {
                    packageFlags = package.Summary.PackageFlags.ToString(),
                    totalHeaderSize = package.Summary.TotalHeaderSize,
                    nameCount = package.Summary.NameCount,
                    exportCount = package.Summary.ExportCount,
                    importCount = package.Summary.ImportCount,
                    packageSource = package.Summary.PackageSource.ToString(),
                    fileVersionUE = new
                    {
                        fileVersionUE4 = package.Summary.FileVersionUE.FileVersionUE4,
                        fileVersionUE5 = package.Summary.FileVersionUE.FileVersionUE5
                    }
                },
                isFullyLoaded = package.IsFullyLoaded,
                exportCount = exports.Length,
                exports = exportsList
            };

            _logger.LogInformation("Successfully loaded file {Path} with {ExportCount} exports", path, exports.Length);
            return packageInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file info");
            return new { error = ex.Message, stackTrace = ex.StackTrace };
        }
    }
}
