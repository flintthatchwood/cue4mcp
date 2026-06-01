using System.ComponentModel;
using System.Text.RegularExpressions;

using CUE4Mcp.Domain;

using ModelContextProtocol.Server;

namespace CUE4Mcp.Tools;

[McpServerToolType]
public class IndexSearchTool
{
    private readonly ExportIndex _index;

    public IndexSearchTool(ExportIndex index)
    {
        _index = index;
    }

    [McpServerTool(Name = "cue4-index-lookup")]
    [Description("Look up export metadata for a package from the cached index. Returns Class, Super, Outer, and Template for each export.")]
    public object IndexLookup(
        [Description("Package path (e.g., 'Game/PrimalEarth/Dinos/Rex/Rex_Character_BP')")]
        string packagePath)
    {
        ExportEntry[]? entries = _index.GetPackageExports(packagePath);
        if (entries == null)
            return new { error = $"Package not found in index: {packagePath}" };

        return new
        {
            packagePath,
            exportCount = entries.Length,
            exports = entries.Select((e, i) => new
            {
                index = i,
                name = e.Name,
                @class = e.Class,
                super = e.Super,
                outer = e.Outer,
                template = e.Template
            }).ToArray()
        };
    }

    [McpServerTool(Name = "cue4-find-by-class")]
    [Description("Find all exports with a given class name. Supports exact match or regex pattern.")]
    public object FindByClass(
        [Description("Class name or regex pattern (e.g., 'BlueprintGeneratedClass' or '.*Character.*')")]
        string pattern,
        [Description("Maximum number of results to return (default 100)")]
        int limit = 100)
    {
        return SearchReverse(pattern, limit, "class", _index.GetClassNames(), _index.FindByClass);
    }

    [McpServerTool(Name = "cue4-find-by-super")]
    [Description("Find all exports with a given super (parent) class name. Supports exact match or regex pattern.")]
    public object FindBySuper(
        [Description("Super class name or regex pattern")]
        string pattern,
        [Description("Maximum number of results to return (default 100)")]
        int limit = 100)
    {
        return SearchReverse(pattern, limit, "super", _index.GetSuperNames(), _index.FindBySuper);
    }

    [McpServerTool(Name = "cue4-find-by-outer")]
    [Description("Find all exports with a given outer (owner/container) name. Supports exact match or regex pattern.")]
    public object FindByOuter(
        [Description("Outer name or regex pattern")]
        string pattern,
        [Description("Maximum number of results to return (default 100)")]
        int limit = 100)
    {
        return SearchReverse(pattern, limit, "outer", _index.GetOuterNames(), _index.FindByOuter);
    }

    [McpServerTool(Name = "cue4-find-by-template")]
    [Description("Find all exports with a given template name. Supports exact match or regex pattern.")]
    public object FindByTemplate(
        [Description("Template name or regex pattern")]
        string pattern,
        [Description("Maximum number of results to return (default 100)")]
        int limit = 100)
    {
        return SearchReverse(pattern, limit, "template", _index.GetTemplateNames(), _index.FindByTemplate);
    }

    [McpServerTool(Name = "cue4-index-stats")]
    [Description("Get statistics about the export index: total packages, exports, and unique values per field.")]
    public object IndexStats()
    {
        return new
        {
            packages = _index.PackageCount,
            exports = _index.ExportCount,
            uniqueClasses = _index.GetClassNames().Count,
            uniqueSupers = _index.GetSuperNames().Count,
            uniqueOuters = _index.GetOuterNames().Count,
            uniqueTemplates = _index.GetTemplateNames().Count,
        };
    }

    [McpServerTool(Name = "cue4-list-classes")]
    [Description("List all distinct class names in the index, with optional regex filter.")]
    public object ListClasses(
        [Description("Optional regex pattern to filter class names")]
        string? pattern = null)
    {
        return FilterNames(_index.GetClassNames(), pattern, _index.FindByClass);
    }

    [McpServerTool(Name = "cue4-list-supers")]
    [Description("List all distinct super (parent) class names in the index, with optional regex filter.")]
    public object ListSupers(
        [Description("Optional regex pattern to filter super names")]
        string? pattern = null)
    {
        return FilterNames(_index.GetSuperNames(), pattern, _index.FindBySuper);
    }

    private static object SearchReverse(
        string pattern,
        int limit,
        string fieldName,
        IReadOnlyCollection<string> allNames,
        Func<string, IReadOnlyList<ExportRef>> lookup)
    {
        // Try exact match first
        IReadOnlyList<ExportRef> exactRefs = lookup(pattern);
        if (exactRefs.Count > 0)
        {
            return new
            {
                field = fieldName,
                matchedValue = pattern,
                totalMatches = exactRefs.Count,
                results = exactRefs.Take(limit).Select(r => new
                {
                    packagePath = r.PackagePath,
                    exportIndex = r.ExportIndex
                }).ToArray()
            };
        }

        // Fall back to regex
        Regex regex = new(pattern, RegexOptions.IgnoreCase);
        string[] matchingNames = allNames.Where(n => regex.IsMatch(n)).ToArray();

        if (matchingNames.Length == 0)
            return new { field = fieldName, pattern, error = "No matching values found" };

        List<object> results = [];
        int total = 0;

        foreach (string name in matchingNames)
        {
            IReadOnlyList<ExportRef> refs = lookup(name);
            total += refs.Count;

            foreach (ExportRef r in refs)
            {
                if (results.Count >= limit) break;
                results.Add(new
                {
                    packagePath = r.PackagePath,
                    exportIndex = r.ExportIndex,
                    value = name
                });
            }

            if (results.Count >= limit) break;
        }

        return new
        {
            field = fieldName,
            pattern,
            matchedValues = matchingNames.Length,
            totalMatches = total,
            results = results.ToArray()
        };
    }

    private static object FilterNames(
        IReadOnlyCollection<string> names,
        string? pattern,
        Func<string, IReadOnlyList<ExportRef>> lookup)
    {
        IEnumerable<string> filtered = names;

        if (!string.IsNullOrWhiteSpace(pattern))
        {
            Regex regex = new(pattern, RegexOptions.IgnoreCase);
            filtered = filtered.Where(n => regex.IsMatch(n));
        }

        return filtered
            .Select(n => new { name = n, count = lookup(n).Count })
            .OrderByDescending(x => x.count)
            .ToArray();
    }
}
