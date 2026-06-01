using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;

using Microsoft.Extensions.Logging;

namespace CUE4Mcp.Domain;

/// <summary>
/// Bidirectional in-memory index of export metadata (Class, Super, Outer, Template).
/// Cached to disk with hash-based validation against pak files.
/// </summary>
public class ExportIndex
{
    private readonly ILogger<ExportIndex> _logger;
    private readonly string _cacheDirectory;

    // Forward index: (packagePath, exportIndex) -> ExportEntry
    private readonly Dictionary<string, ExportEntry[]> _forward = new(StringComparer.OrdinalIgnoreCase);

    // Reverse indices: field value -> list of (packagePath, exportIndex)
    private readonly Dictionary<string, List<ExportRef>> _byClass = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ExportRef>> _bySuper = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ExportRef>> _byOuter = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ExportRef>> _byTemplate = new(StringComparer.OrdinalIgnoreCase);

    public int PackageCount => _forward.Count;
    public int ExportCount => _forward.Values.Sum(e => e.Length);

    public ExportIndex(ILogger<ExportIndex> logger, string cacheDirectory)
    {
        _logger = logger;
        _cacheDirectory = cacheDirectory;
    }

    /// <summary>
    /// Loads index from cache if valid, otherwise builds from provider and saves to cache.
    /// </summary>
    public async Task InitializeAsync(DefaultFileProvider provider)
    {
        string hash = ComputePakHash(provider);
        string cachePath = Path.Combine(_cacheDirectory, "export-index.json.gz");
        string hashPath = Path.Combine(_cacheDirectory, "export-index.hash");

        if (File.Exists(cachePath) && File.Exists(hashPath))
        {
            string cachedHash = await File.ReadAllTextAsync(hashPath);
            if (cachedHash == hash)
            {
                _logger.LogInformation("Loading export index from cache");
                await LoadFromCacheAsync(cachePath);
                _logger.LogInformation("Loaded index: {Packages} packages, {Exports} exports", PackageCount, ExportCount);
                return;
            }

            _logger.LogInformation("Pak hash mismatch, rebuilding index");
        }

        Build(provider);

        Directory.CreateDirectory(_cacheDirectory);
        await SaveToCacheAsync(cachePath);
        await File.WriteAllTextAsync(hashPath, hash);
        _logger.LogInformation("Built and cached index: {Packages} packages, {Exports} exports", PackageCount, ExportCount);
    }

    /// <summary>
    /// Gets all export entries for a package.
    /// </summary>
    public ExportEntry[]? GetPackageExports(string packagePath)
    {
        return _forward.TryGetValue(packagePath, out ExportEntry[]? entries) ? entries : null;
    }

    /// <summary>
    /// Finds all exports with a given class name.
    /// </summary>
    public IReadOnlyList<ExportRef> FindByClass(string className)
    {
        return _byClass.TryGetValue(className, out List<ExportRef>? refs) ? refs : [];
    }

    /// <summary>
    /// Finds all exports with a given super name.
    /// </summary>
    public IReadOnlyList<ExportRef> FindBySuper(string superName)
    {
        return _bySuper.TryGetValue(superName, out List<ExportRef>? refs) ? refs : [];
    }

    /// <summary>
    /// Finds all exports with a given outer name.
    /// </summary>
    public IReadOnlyList<ExportRef> FindByOuter(string outerName)
    {
        return _byOuter.TryGetValue(outerName, out List<ExportRef>? refs) ? refs : [];
    }

    /// <summary>
    /// Finds all exports with a given template name.
    /// </summary>
    public IReadOnlyList<ExportRef> FindByTemplate(string templateName)
    {
        return _byTemplate.TryGetValue(templateName, out List<ExportRef>? refs) ? refs : [];
    }

    /// <summary>
    /// Gets all distinct class names in the index.
    /// </summary>
    public IReadOnlyCollection<string> GetClassNames() => _byClass.Keys;

    /// <summary>
    /// Gets all distinct super names in the index.
    /// </summary>
    public IReadOnlyCollection<string> GetSuperNames() => _bySuper.Keys;

    /// <summary>
    /// Gets all distinct outer names in the index.
    /// </summary>
    public IReadOnlyCollection<string> GetOuterNames() => _byOuter.Keys;

    /// <summary>
    /// Gets all distinct template names in the index.
    /// </summary>
    public IReadOnlyCollection<string> GetTemplateNames() => _byTemplate.Keys;

    private void Build(DefaultFileProvider provider)
    {
        _forward.Clear();
        _byClass.Clear();
        _bySuper.Clear();
        _byOuter.Clear();
        _byTemplate.Clear();

        string[] packageFiles = provider.Files.Keys
            .Where(IsPackageFile)
            .ToArray();

        _logger.LogInformation("Building export index for {Count} package files", packageFiles.Length);

        int processed = 0;
        int errors = 0;

        foreach (string filePath in packageFiles)
        {
            try
            {
                IPackage package = provider.LoadPackage(filePath);
                if (package == null) continue;

                Lazy<UObject>[] exports = package.ExportsLazy;
                ExportEntry[] entries = new ExportEntry[exports.Length];

                for (int i = 0; i < exports.Length; i++)
                {
                    try
                    {
                        UObject export = exports[i].Value;
                        string? className = export.Class?.Name;
                        string? superName = export.Super?.Name.ToString();
                        string? outerName = export.Outer?.Name;
                        string? templateName = export.Template?.Name.ToString();
                        string exportName = export.Name;

                        entries[i] = new ExportEntry(exportName, className, superName, outerName, templateName);

                        ExportRef exportRef = new(package.Name, i);

                        if (className != null)
                            AddToReverse(_byClass, className, exportRef);
                        if (superName != null)
                            AddToReverse(_bySuper, superName, exportRef);
                        if (outerName != null)
                            AddToReverse(_byOuter, outerName, exportRef);
                        if (templateName != null)
                            AddToReverse(_byTemplate, templateName, exportRef);
                    }
                    catch
                    {
                        entries[i] = new ExportEntry(null, null, null, null, null);
                        errors++;
                    }
                }

                _forward[package.Name] = entries;
                processed++;

                if (processed % 10000 == 0)
                    _logger.LogInformation("Indexed {Processed}/{Total} packages ({Errors} errors)", processed, packageFiles.Length, errors);
            }
            catch
            {
                errors++;
            }
        }

        _logger.LogInformation("Index build complete: {Processed} packages, {Errors} errors", processed, errors);
    }

    private static void AddToReverse(Dictionary<string, List<ExportRef>> index, string key, ExportRef exportRef)
    {
        if (!index.TryGetValue(key, out List<ExportRef>? list))
        {
            list = [];
            index[key] = list;
        }

        list.Add(exportRef);
    }

    private async Task SaveToCacheAsync(string path)
    {
        // Build string table: assign an index to each unique string
        Dictionary<string, int> stringToId = new(StringComparer.Ordinal);
        List<string> strings = [];

        int Intern(string? s)
        {
            if (s == null) return -1;
            if (stringToId.TryGetValue(s, out int id)) return id;
            id = strings.Count;
            strings.Add(s);
            stringToId[s] = id;
            return id;
        }

        // Convert forward index to compact form
        List<CachePackage> packages = new(_forward.Count);
        foreach (KeyValuePair<string, ExportEntry[]> kvp in _forward)
        {
            int nameId = Intern(kvp.Key);
            int[][] exports = new int[kvp.Value.Length][];
            for (int i = 0; i < kvp.Value.Length; i++)
            {
                ExportEntry e = kvp.Value[i];
                exports[i] = [Intern(e.Name), Intern(e.Class), Intern(e.Super), Intern(e.Outer), Intern(e.Template)];
            }

            packages.Add(new CachePackage { Name = nameId, Exports = exports });
        }

        CacheData data = new() { Strings = [.. strings], Packages = [.. packages] };

        await using FileStream fileStream = File.Create(path);
        await using GZipStream gzip = new(fileStream, CompressionLevel.Optimal);
        await JsonSerializer.SerializeAsync(gzip, data);
    }

    private async Task LoadFromCacheAsync(string path)
    {
        _forward.Clear();
        _byClass.Clear();
        _bySuper.Clear();
        _byOuter.Clear();
        _byTemplate.Clear();

        await using FileStream fileStream = File.OpenRead(path);
        await using GZipStream gzip = new(fileStream, CompressionMode.Decompress);
        CacheData? data = await JsonSerializer.DeserializeAsync<CacheData>(gzip);

        if (data?.Packages == null || data.Strings == null)
            return;

        string[] strings = data.Strings;

        string? Resolve(int id) => id < 0 ? null : strings[id];

        foreach (CachePackage pkg in data.Packages)
        {
            string packageName = strings[pkg.Name];
            ExportEntry[] entries = new ExportEntry[pkg.Exports.Length];

            for (int i = 0; i < pkg.Exports.Length; i++)
            {
                int[] e = pkg.Exports[i];
                entries[i] = new ExportEntry(Resolve(e[0]), Resolve(e[1]), Resolve(e[2]), Resolve(e[3]), Resolve(e[4]));

                ExportRef exportRef = new(packageName, i);

                if (entries[i].Class != null)
                    AddToReverse(_byClass, entries[i].Class!, exportRef);
                if (entries[i].Super != null)
                    AddToReverse(_bySuper, entries[i].Super!, exportRef);
                if (entries[i].Outer != null)
                    AddToReverse(_byOuter, entries[i].Outer!, exportRef);
                if (entries[i].Template != null)
                    AddToReverse(_byTemplate, entries[i].Template!, exportRef);
            }

            _forward[packageName] = entries;
        }
    }

    private static string ComputePakHash(DefaultFileProvider provider)
    {
        // Build a hash from pak file names and sizes to detect changes
        IOrderedEnumerable<string> pakFiles = provider.MountedVfs
            .Select(vfs => $"{vfs.Name}:{vfs.Length}")
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

        string combined = string.Join('\n', pakFiles);
        byte[] hashBytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexStringLower(hashBytes);
    }

    private static bool IsPackageFile(string filePath)
    {
        foreach (string extension in GameFile.UePackageExtensions)
        {
            if (filePath.EndsWith($".{extension}", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    // Cache serialization types (string table + int references)
    private class CacheData
    {
        public string[] Strings { get; set; } = [];
        public CachePackage[] Packages { get; set; } = [];
    }

    private class CachePackage
    {
        public int Name { get; set; }
        public int[][] Exports { get; set; } = []; // Each export: [name, class, super, outer, template] as string table indices
    }
}

public record struct ExportEntry(string? Name, string? Class, string? Super, string? Outer, string? Template);
public record struct ExportRef(string PackagePath, int ExportIndex);
