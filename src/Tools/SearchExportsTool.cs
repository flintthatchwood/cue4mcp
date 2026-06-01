using System.ComponentModel;
using System.Diagnostics;

using CUE4Mcp.Domain;

using Microsoft.Extensions.Logging;

using ModelContextProtocol.Server;

namespace CUE4Mcp.Tools;

[McpServerToolType]
public class SearchExportsTool
{
    private readonly FileService _fileService;
    private readonly ILogger<SearchExportsTool> _logger;
    private const int _maxMatches = 100;

    public SearchExportsTool(ILogger<SearchExportsTool> logger, FileService fileService)
    {
        _fileService = fileService;
        _logger = logger;
    }

    [McpServerTool(Name = "cue4-search-exports")]
    [Description("Search for exports by key name or property value. Returns matching exports with their package names.")]
    public async Task<object> SearchExports(
        [Description("Optional regex pattern to match export property key names (e.g., '^PrecomputedVolumeDistanceField$', '^Blueprint.*')")]
        string? keyPattern = null,
        [Description("Optional regex pattern to match property values as strings (e.g., '.*BlackPearl.*', '^Weapon_.*')")]
        string? valuePattern = null,
        [Description("Optional regex pattern to filter which packages to search (e.g., '.*Maps.*', 'Game/Characters/.*'). Matches against package names.")]
        string? packagePattern = null,
        [Description("Type of search: 'field' (search export fields), 'property' (search export properties), or 'all' (both). Default: 'all'")]
        string searchType = "all",
        [Description("Optional cursor for pagination (opaque string)")]
        string? cursor = null)
    {
        try
        {
            // Validate return level
            if (!Enum.TryParse<ExportSearchType>(searchType, true, out ExportSearchType exportSearchType))
            {
                return new { error = "searchType must be 'field', 'property', or 'all'", code = -32602 };
            }

            // Parse cursor (file skip position)
            int skip = Cursor.Parse(cursor);

            Stopwatch stopwatch = Stopwatch.StartNew();
            // Get search results from domain service
            List<SearchResult> searchResults = _fileService.SearchExports(keyPattern, valuePattern, packagePattern, exportSearchType).ToList();

            // Apply pagination and collect matches
            IEnumerable<SearchResult> page = searchResults
                .Skip(skip)
                .Take(_maxMatches);

            var matches = page
                .Select(x => new { Package = x.Package.Name, x.ExportIndex, Export = x.Export.Name, x.SearchType, x.MatchType, x.MatchedName, x.MatchedValue })
                .ToList();
            stopwatch.Stop();

            int nextSkip = skip + matches.Count;

            IEnumerable<string> packageCount = matches.Select(x => x.Package).Distinct();

            _logger.LogInformation("Found {MatchCount} matches across {PackageCount} packages", matches.Count, packageCount);

            // Build response with counts and continuation token
            Dictionary<string, object> response = new()
            {
                ["searchTime"] = stopwatch.ElapsedMilliseconds,
                ["matchCount"] = matches.Count,
                ["totalMatches"] = searchResults.Count,
                ["matches"] = matches
            };

            bool hasMore = nextSkip < searchResults.Count;
            if (hasMore)
            {
                response["nextCursor"] = Cursor.Encode(nextSkip);
            }

            return response;
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid argument");
            return new { error = ex.Message, code = -32602 };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching exports");
            return new { error = ex.Message, stackTrace = ex.StackTrace };
        }
    }
}
