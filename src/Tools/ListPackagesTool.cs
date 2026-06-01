using System.ComponentModel;

using CUE4Mcp.Domain;

using Microsoft.Extensions.Logging;

using ModelContextProtocol.Server;

namespace CUE4Mcp.Tools;

[McpServerToolType]
public class ListPackagesTool
{
    private readonly FileService _fileService;
    private readonly ILogger<ListPackagesTool> _logger;
    private const int PageSize = 250;

    public ListPackagesTool(ILogger<ListPackagesTool> logger, FileService fileService)
    {
        _fileService = fileService;
        _logger = logger;
    }

    [McpServerTool(Name = "cue4-list-packages")]
    [Description("List Unreal Engine packages from the file provider. Returns package names (logical Unreal paths like '/Game/Maps/TheIsland').")]
    public async Task<object> ListPackages(
        [Description(@"Optional regex pattern to filter package names (e.g., '.*Maps.*', 'Game/Characters/.*'). Matches against package names, not file paths.")]
        string pattern = "",
        [Description("Optional cursor for pagination (opaque string)")]
        string? cursor = null)
    {
        try
        {
            // Get filtered packages from domain service
            string[] allPackages = _fileService.ListPackages(string.IsNullOrWhiteSpace(pattern) ? null : pattern).ToArray();

            // Parse cursor (skip position)
            int skip = Cursor.Parse(cursor);

            // Apply pagination
            string[] pagedPackages = allPackages.Skip(skip).Take(PageSize).ToArray();
            int nextSkip = skip + PageSize;

            _logger.LogInformation("Returning {Count} packages starting at {Skip} (total: {Total})",
                pagedPackages.Length, skip, allPackages.Length);

            // Build response with counts and continuation token
            Dictionary<string, object> response = new()
            {
                ["success"] = true,
                ["totalPackageCount"] = allPackages.Length,
                ["filteredPackageCount"] = allPackages.Length,
                ["packages"] = pagedPackages
            };

            // Add nextCursor if there are more results
            if (nextSkip < allPackages.Length)
            {
                response["nextCursor"] = Cursor.Encode(nextSkip);
            }

            return response;
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid regex pattern");
            return new { error = ex.Message, code = -32602 };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing packages");
            return new { error = ex.Message, stackTrace = ex.StackTrace };
        }
    }
}
