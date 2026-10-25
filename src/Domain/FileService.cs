using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

using CUE4Parse.Compression;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.Core.i18N;
using CUE4Parse.UE4.Objects.UObject;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace CUE4Mcp.Domain;

/// <summary>
/// Domain service that manages file provider initialization and provides business logic for package operations
/// </summary>
public class FileService
{
    private readonly FileServiceOptions _options;
    private readonly ILogger<FileService> _logger;
    private readonly IExportIndex _index;
    private DefaultFileProvider? _fileProvider;

    public DefaultFileProvider Provider
    {
        get
        {
            EnsureInitialized();
            return _fileProvider;
        }
    }

    public FileService(ILogger<FileService> logger, IOptions<FileServiceOptions> options, IExportIndex index)
    {
        _logger = logger;
        _options = options.Value;
        _index = index;
    }

    /// <summary>
    /// Initializes the file provider with the configured options
    /// </summary>
    public async Task InitializeAsync()
    {
        string directory = _options.Directory;
        string gameString = _options.Game;
        string searchOptionString = _options.SearchOption;
        string? aesKey = _options.AesKey;

        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException("FileProvider directory is not configured");
        }

        if (string.IsNullOrEmpty(gameString))
        {
            throw new ArgumentException("FileProvider game is not configured");
        }

        if (!Enum.TryParse(gameString, out CUE4Parse.UE4.Versions.EGame game)
            && !Enum.TryParse("GAME_" + gameString, out game))
        {
            throw new ArgumentException("FileProvider game is not valid");
        }

        await InitializeOodleAsync();

        SearchOption searchOption = string.IsNullOrEmpty(searchOptionString)
            ? SearchOption.TopDirectoryOnly
            : Enum.Parse<SearchOption>(searchOptionString);

        _logger.LogInformation("Creating DefaultFileProvider for {Directory} with {Game}", directory, game);

        _fileProvider = new DefaultFileProvider(
            directory,
            searchOption,
            new CUE4Parse.UE4.Versions.VersionContainer(game),
            StringComparer.OrdinalIgnoreCase);

        // Load property mappings if available
        string? mappingsPath = _options.MappingsPath;
        if (!string.IsNullOrEmpty(mappingsPath) && File.Exists(mappingsPath))
        {
            _logger.LogInformation("Loading mappings from {MappingsPath}", mappingsPath);
            _fileProvider.MappingsContainer = new FileUsmapTypeMappingsProvider(mappingsPath);
        }

        _fileProvider.Initialize();

        if (!string.IsNullOrWhiteSpace(aesKey))
        {
            string cleanKey = aesKey.Replace("0x", "").Replace("0X", "");
            _fileProvider.SubmitKey(
                new CUE4Parse.UE4.Objects.Core.Misc.FGuid(),
                new CUE4Parse.Encryption.Aes.FAesKey(cleanKey));
            _logger.LogInformation("AES key submitted");
        }

        await _fileProvider.MountAsync();
        _logger.LogInformation("Provider mounted with {Count} files", _fileProvider.Files.Count);
    }

    /// <summary>
    /// Lists package names with optional regex pattern filtering
    /// </summary>
    /// <param name="pattern">Optional regex pattern to filter package names</param>
    /// <returns>Enumerable of package names (logical Unreal Engine paths)</returns>
    public IReadOnlyList<string> ListPackages(string? pattern = null)
    {
        EnsureInitialized();

        IEnumerable<string> packageNames = _index.PackagePaths;

        if (!string.IsNullOrWhiteSpace(pattern))
        {
            Regex regex = new(pattern, RegexOptions.IgnoreCase);
            packageNames = packageNames.Where(p => regex.IsMatch(p));
        }

        return packageNames.ToArray();
    }

    /// <summary>
    /// Gets package information including exports
    /// </summary>
    /// <param name="packageName">Package name (e.g., 'Game/Maps/TheIsland' or with file path prefix)</param>
    public IPackage GetPackage(string packageName)
    {
        EnsureInitialized();

        if (string.IsNullOrWhiteSpace(packageName))
            throw new ArgumentException("Package name is required", nameof(packageName));

        if (packageName.Contains('\\'))
            throw new ArgumentException("Package name must use forward slashes (/) as separators, not backslashes (\\)", nameof(packageName));

        _logger.LogInformation("Loading package: {PackageName}", packageName);

        IPackage package = _fileProvider.LoadPackage(packageName);
        if (package == null)
            throw new InvalidOperationException($"Package not found: {packageName}");

        _logger.LogInformation("Successfully loaded package {PackageName} (actual name: {ActualName})", packageName, package.Name);
        return package;
    }

    /// <summary>
    /// Gets a specific export from a package as JSON
    /// </summary>
    /// <param name="packageName">Package name (e.g., 'Game/Maps/TheIsland')</param>
    /// <param name="exportIndex">Zero-based index of the export</param>
    public UObject GetExport(string packageName, int exportIndex)
    {
        EnsureInitialized();

        if (string.IsNullOrWhiteSpace(packageName))
            throw new ArgumentException("Package name is required", nameof(packageName));

        if (packageName.Contains('\\'))
            throw new ArgumentException("Package name must use forward slashes (/) as separators, not backslashes (\\)", nameof(packageName));

        if (exportIndex < 0)
            throw new ArgumentException("Export index must be non-negative", nameof(exportIndex));

        _logger.LogInformation("Loading export {Index} from package: {PackageName}", exportIndex, packageName);

        IPackage package = _fileProvider.LoadPackage(packageName);
        if (package == null)
            throw new InvalidOperationException($"Package not found: {packageName}");

        Lazy<UObject>[] exports = package.ExportsLazy;
        if (exportIndex >= exports.Length)
            throw new ArgumentException($"Export index {exportIndex} is out of range. Package has {exports.Length} exports.", nameof(exportIndex));

        UObject export = exports[exportIndex].Value;

        _logger.LogInformation("Successfully loaded export {Index} ({Name}) from {PackageName}", exportIndex, export.Name, packageName);
        return export;
    }

    /// <summary>
    /// Exports packages matching a regex filter to disk
    /// </summary>
    /// <param name="pattern">Regex pattern to match package names</param>
    public async Task<IReadOnlyList<string>> ExportPackagesToDiskAsync(string pattern, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        if (string.IsNullOrWhiteSpace(pattern))
            throw new ArgumentException("Pattern is required", nameof(pattern));

        _logger.LogInformation("Exporting packages to disk: {Pattern}", pattern);

        Regex regex = new(pattern, RegexOptions.IgnoreCase);
        List<string> exportedFiles = new();

        string[] matchingNames = _index.PackagePaths
            .Where(name => regex.IsMatch(name))
            .ToArray();

        foreach (string packageName in matchingNames)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                IPackage package = _fileProvider.LoadPackage(packageName);
                if (package != null)
                {
                    exportedFiles.Add(await ExportPackageAsync(package, packageName));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to export package: {PackageName}", packageName);
            }
        }

        return exportedFiles;
    }

    /// <summary>
    /// Exports the complete package list to disk
    /// </summary>
    public async Task<string> ExportPackageListToDiskAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        _logger.LogInformation("Exporting complete package list to disk");

        string[] packageNames = _index.PackagePaths.ToArray();

        string outputDirectory = _options.OutputDirectory;

        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        string outputPath = Path.Combine(outputDirectory, "all-packages.json");

        string json = JsonConvert.SerializeObject(new
        {
            success = true,
            totalPackageCount = packageNames.Length,
            exportedAt = DateTime.UtcNow,
            packages = packageNames
        }, Formatting.Indented);

        await File.WriteAllTextAsync(outputPath, json, cancellationToken);

        _logger.LogInformation("Successfully exported {Count} packages to {Path}", packageNames.Length, outputPath);
        return outputPath;
    }

    /// <summary>
    /// Searches for exports matching key or value patterns
    /// </summary>
    /// <param name="keyPattern">Optional regex pattern to match property key names</param>
    /// <param name="valuePattern">Optional regex pattern to match property values</param>
    /// <param name="packagePattern">Optional regex pattern to filter package names</param>
    /// <param name="searchType">The locations to search</param>
    public IEnumerable<SearchResult> SearchExports(
        string? packagePattern = null,
        string? keyPattern = null,
        string? valuePattern = null,
        ExportSearchType searchType = ExportSearchType.All,
        string? classPattern = null,
        string? superPattern = null,
        string? outerPattern = null,
        string? templatePattern = null
    )
    {
        EnsureInitialized();

        if (string.IsNullOrWhiteSpace(keyPattern) && string.IsNullOrWhiteSpace(valuePattern))
            throw new ArgumentException("At least one search pattern (keyPattern or valuePattern) is required");

        static Regex? CompilePattern(string? pattern)
        {
            return string.IsNullOrWhiteSpace(pattern)
                ? null
                : new Regex(pattern, RegexOptions.IgnoreCase);
        }

        Regex? keyRegex = CompilePattern(keyPattern);
        Regex? valueRegex = CompilePattern(valuePattern);
        Regex? packageRegex = CompilePattern(packagePattern);

        _logger.LogInformation(
            "Searching exports - Key: {KeyPattern}, Value: {ValuePattern}, Package: {PackagePattern}, Class: {ClassPattern}, Super: {SuperPattern}",
            keyPattern ?? "(none)", valuePattern ?? "(none)", packagePattern ?? "(all)",
            classPattern ?? "(none)", superPattern ?? "(none)");

        // Use index to pre-filter candidate packages when index patterns are provided.
        // The property-key index is only consulted when the user restricted the search
        // to Property scope — otherwise a key could match a top-level UObject field
        // on a package that has no matching property tag, and the index would wrongly
        // exclude it.
        string? indexKeyPattern = searchType == ExportSearchType.Property ? keyPattern : null;
        HashSet<string>? indexCandidates = BuildIndexCandidates(
            classPattern, superPattern, outerPattern, templatePattern, indexKeyPattern);

        if (indexCandidates != null)
        {
            // Fast path: only load packages identified by the index
            IEnumerable<string> candidates = indexCandidates;
            if (packageRegex != null)
                candidates = candidates.Where(name => packageRegex.IsMatch(name));

            foreach (string packageName in candidates)
            {
                IPackage? package;
                try { package = _fileProvider.LoadPackage(packageName); }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to load package: {Name}", packageName); continue; }
                if (package == null) continue;

                foreach (SearchResult result in SearchPackageExports(package, keyRegex, valueRegex, searchType))
                    yield return result;
            }
        }
        else
        {
            // Slow path: scan all package files
            foreach (string filePath in _fileProvider.Files.Keys.Where(IsPackageFile))
            {
                if (!_fileProvider.TryLoadPackage(filePath, out IPackage? package))
                    continue;

                if (packageRegex != null && !packageRegex.IsMatch(package.Name))
                    continue;

                foreach (SearchResult result in SearchPackageExports(package, keyRegex, valueRegex, searchType))
                    yield return result;
            }
        }
    }

    private IEnumerable<SearchResult> SearchPackageExports(IPackage package, Regex? keyRegex, Regex? valueRegex, ExportSearchType searchType)
    {
        Lazy<UObject>[] exports = package.ExportsLazy;
        for (int j = 0; j < exports.Length; j++)
        {
            UObject export;
            try { export = exports[j].Value; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to load export {Index} from {PackageName}", j, package.Name);
                continue;
            }

            foreach (ExportSearchResult match in SearchExport(export, keyRegex, valueRegex, searchType))
                yield return new SearchResult(package, export, j, match.SearchType, match.MatchType, match.MatchedName, match.MatchedValue);
        }
    }

    private HashSet<string>? BuildIndexCandidates(
        string? classPattern,
        string? superPattern,
        string? outerPattern,
        string? templatePattern,
        string? propertyKeyPattern)
    {
        if (classPattern == null && superPattern == null && outerPattern == null
            && templatePattern == null && propertyKeyPattern == null)
            return null;

        HashSet<string>? result = null;

        void Intersect(IEnumerable<string> paths)
        {
            HashSet<string> set = new(paths, StringComparer.OrdinalIgnoreCase);
            if (result == null)
                result = set;
            else
                result.IntersectWith(set);
        }

        if (classPattern != null)
            Intersect(GetPackagesByIndexPattern(classPattern, _index.GetClassNames(), _index.FindByClass));
        if (superPattern != null)
            Intersect(GetPackagesByIndexPattern(superPattern, _index.GetSuperNames(), _index.FindBySuper));
        if (outerPattern != null)
            Intersect(GetPackagesByIndexPattern(outerPattern, _index.GetOuterNames(), _index.FindByOuter));
        if (templatePattern != null)
            Intersect(GetPackagesByIndexPattern(templatePattern, _index.GetTemplateNames(), _index.FindByTemplate));
        if (propertyKeyPattern != null)
            Intersect(GetPackagesByIndexPattern(propertyKeyPattern, _index.GetPropertyKeys(), _index.FindByPropertyKey));

        return result ?? []; // empty set = patterns had no index matches
    }

    private static IEnumerable<string> GetPackagesByIndexPattern(
        string pattern,
        IReadOnlyCollection<string> indexNames,
        Func<string, IReadOnlyList<ExportRef>> lookup)
    {
        Regex regex = new(pattern, RegexOptions.IgnoreCase);
        return indexNames
            .Where(n => regex.IsMatch(n))
            .SelectMany(lookup)
            .Select(r => r.PackagePath);
    }

    // guarantees that _fileProvider is not null on exit

    [MemberNotNull(nameof(_fileProvider))]
    private void EnsureInitialized()
    {
        if (_fileProvider == null)
        {
            throw new InvalidOperationException("FileService not initialized. Call InitializeAsync first.");
        }
    }

    private bool IsPackageFile(string filePath)
    {
        // Only consider .uasset and .umap files as packages
        // .uexp, .ubulk, etc. are supporting files

        foreach (string extension in GameFile.UePackageExtensions)
        {
            if (filePath.EndsWith($".{extension}", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private async Task InitializeOodleAsync()
    {
        string nativeOodleFilename = Environment.OSVersion.Platform switch
        {
            PlatformID.Win32NT => "oo2core_9_win64.dll",
            PlatformID.Unix => "liboo2corelinux64.so.9",
            _ => throw new PlatformNotSupportedException("Unsupported OS for Oodle"),
        };

        string oodleLocation = Path.Join(AppContext.BaseDirectory, nativeOodleFilename);
        _logger.LogInformation("Initializing Oodle from {OodleLocation}", oodleLocation);

        if (!File.Exists(oodleLocation))
        {
            throw new PlatformNotSupportedException($"{nativeOodleFilename} not found.");
        }

        OodleHelper.Initialize(oodleLocation);
    }

    private async Task<string> ExportPackageAsync(IPackage package, string filePath)
    {
        string outputDirectory = _options.OutputDirectory;
        // Use package name for output path structure
        string sanitizedPath = Regex.Replace(package.Name, @"[^\w-\./]", "_").TrimStart('/');
        string outputPath = Path.Combine(outputDirectory, Path.ChangeExtension(sanitizedPath, ".json"));

        if (File.Exists(outputPath))
        {
            _logger.LogInformation("Package already exported, skipping: {PackageName}", package.Name);
            return outputPath;
        }

        string parentDirectory = Path.GetDirectoryName(outputPath)!;
        if (!Directory.Exists(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        UObject[] exports = package.GetExports().ToArray();

        string json = JsonConvert.SerializeObject(new
        {
            packageName = package.Name,
            filePath = filePath,
            package.Summary,
            Exports = exports
        }, Formatting.Indented);

        await File.WriteAllTextAsync(outputPath, json);

        _logger.LogInformation("Successfully exported package {PackageName} with {ExportCount} exports to {OutputPath}",
            package.Name, exports.Length, outputPath);

        return outputPath;
    }

    private List<ExportSearchResult> SearchExport(UObject export, Regex? keyRegex, Regex? valueRegex, ExportSearchType searchType)
    {
        List<ExportSearchResult> matches = new();
        bool hasProperties = export.Properties?.Count > 0;

        if (searchType == ExportSearchType.Property && !hasProperties)
            return matches;

        JObject serialized = JObject.FromObject(export, Json.DefaultSerializer);

        if (searchType.HasFlag(ExportSearchType.Field))
        {
            foreach (JProperty property in serialized.Properties())
            {
                string key = property.Name;
                string value = property.Value.ToString(Formatting.None);

                ExportMatchType matchType = ExportMatchType.None;
                if (keyRegex != null && keyRegex.IsMatch(key))
                {
                    matchType |= ExportMatchType.Key;
                }

                if (key != "Properties" && valueRegex != null && !string.IsNullOrEmpty(value) && valueRegex.IsMatch(value))
                {
                    // We don't value match in the Properties field.
                    matchType |= ExportMatchType.Value;
                }

                if (matchType != ExportMatchType.None)
                {
                    matches.Add(new ExportSearchResult(ExportSearchType.Field, matchType, key, value));
                }
            }
        }

        if (searchType.HasFlag(ExportSearchType.Property) && hasProperties)
        {
            foreach (JProperty property in serialized.Value<JObject>("Properties")!.Properties())
            {
                ExportMatchType matchType = ExportMatchType.None;
                string key = property.Name;
                string value = property.Value.ToString(Formatting.None);

                if (keyRegex != null && keyRegex.IsMatch(key))
                {
                    matchType |= ExportMatchType.Key;
                }

                if (valueRegex != null && valueRegex.IsMatch(value))
                {
                    matchType |= ExportMatchType.Value;
                }

                if (matchType != ExportMatchType.None)
                {
                    matches.Add(new ExportSearchResult(ExportSearchType.Property, matchType, key, value));
                }
            }
        }

        return matches;
    }
}

[Flags]
public enum ExportSearchType
{
    Property = 1,
    Field = 2,
    All = Property | Field
}

[Flags]
public enum ExportMatchType
{
    None = 0,
    Key = 1,
    Value = 2,
    Both = Key | Value
}

public record struct ExportSearchResult(ExportSearchType SearchType, ExportMatchType MatchType, string MatchedName, string? MatchedValue);
public record struct SearchResult(IPackage Package, UObject Export, int ExportIndex, ExportSearchType SearchType, ExportMatchType MatchType, string MatchedName, string? MatchedValue);
