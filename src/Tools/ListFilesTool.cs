using System.ComponentModel;
using System.Text.RegularExpressions;

using CUE4Parse.FileProvider;

using Microsoft.Extensions.Logging;

using ModelContextProtocol.Server;

namespace CUE4Mcp.Tools;

[McpServerToolType]
public class ListFilesTool
{
    private readonly IFileProvider _fileProvider;
    private readonly ILogger<ListFilesTool> _logger;

    public ListFilesTool(ILogger<ListFilesTool> logger, IFileProvider fileProvider)
    {
        _fileProvider = fileProvider;
        _logger = logger;
    }

    [McpServerTool(Name = "cue4-list-files")]
    [Description("List files from a CUE4Parse DefaultFileProvider. Scans Unreal Engine archive files (PAK/UTOC/UCAS) in a directory.")]
    public async Task<object> ListFiles(
        [Description(@"Optional regex pattern to filter results (e.g., '.*\.uasset$', '.*\.umap$', 'ShooterGame/Content/Maps/.*'). Must use Linux-style path separators (/) in patterns.")]
        string pattern = "",
        [Description("Optional cursor for pagination (opaque string)")]
        string? cursor = null)
    {
        try
        {
            // Get file list
            IEnumerable<string> allFiles = _fileProvider.Files.Keys;
            IEnumerable<string> filteredFiles;
        
            // Filter by pattern if provided
            if (!string.IsNullOrWhiteSpace(pattern))
            {
                try
                {
                    var regex = new Regex(pattern, RegexOptions.IgnoreCase);
                    filteredFiles = allFiles.Where(f => regex.IsMatch(f));
                }
                catch (ArgumentException ex)
                {
                    _logger.LogWarning("Invalid regex pattern: {Message}", ex.Message);
                    return new { error = $"Invalid regex pattern: {ex.Message}", code = -32602 };
                }
            }
            else
            {
                filteredFiles = allFiles;
            }

            // Parse cursor (skip position)
            int skip = 0;
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                try
                {
                    var cursorBytes = Convert.FromBase64String(cursor);
                    var cursorText = System.Text.Encoding.UTF8.GetString(cursorBytes);
                    skip = int.Parse(cursorText);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Invalid cursor: {Message}", ex.Message);
                    return new { error = "Invalid cursor", code = -32602 };
                }
            }

            _logger.LogInformation("Getting files matching {Pattern}", pattern);

            // Page size is server-determined (250 items)
            const int pageSize = 250;
            var allFileCount = allFiles.Count();
            var filteredFileCount = filteredFiles.Count();
            var pagedFiles = filteredFiles.Skip(skip).Take(pageSize).ToArray();
            var nextSkip = skip + pageSize;
        
            _logger.LogInformation("Returning {Count} files starting at {Skip} (total filtered: {Total})", pagedFiles.Length, skip, filteredFileCount);

            // Build response with nextCursor if more results exist
            var response = new Dictionary<string, object>
            {
                ["success"] = true,
                ["totalFileCount"] = _fileProvider.Files.Count,
                ["filteredFileCount"] = filteredFileCount,
                ["files"] = pagedFiles
            };

            // Add nextCursor if there are more results
            if (nextSkip < filteredFileCount)
            {
                var nextCursorBytes = System.Text.Encoding.UTF8.GetBytes(nextSkip.ToString());
                var nextCursor = Convert.ToBase64String(nextCursorBytes);
                response["nextCursor"] = nextCursor;
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing files");
            return new { error = ex.Message, stackTrace = ex.StackTrace };
        }
    }
}
