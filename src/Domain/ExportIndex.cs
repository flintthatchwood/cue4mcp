using System.Diagnostics;
using System.Security.Cryptography;

using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace CUE4Mcp.Domain;

/// <summary>
/// SQLite-backed bidirectional index of export metadata (Class, Super, Outer, Template)
/// plus property keys/types. Storage is a single .db file in the cache directory; rebuilt
/// from pak files when the pak hash mismatches the stored hash.
///
/// Schema: interned <c>strings</c> table + <c>packages</c> + <c>exports</c> (FK ids for
/// name/class/super/outer/template) + <c>properties</c> (export_id + key_id + type_id).
/// </summary>
public sealed class ExportIndex : IExportIndex, IDisposable
{
    private readonly ILogger<ExportIndex> _logger;
    private readonly string _cacheDirectory;
    private readonly string _dbPath;

    private SqliteConnection? _conn;

    // Loaded once at init from packages table; small enough (~1.3 MB at full game)
    private string[] _packagePaths = [];
    private Dictionary<string, long> _packageIdByPath = new(StringComparer.OrdinalIgnoreCase);
    private string[] _packagePathById = []; // index = id - 1 (packages.id is 1-based contiguous)

    private int _exportCount;
    private int _propertyCount;

    // Lazy prepared statements for hot lookups
    private SqliteCommand? _stmtFindByClass;
    private SqliteCommand? _stmtFindBySuper;
    private SqliteCommand? _stmtFindByOuter;
    private SqliteCommand? _stmtFindByTemplate;
    private SqliteCommand? _stmtFindByPropKey;
    private SqliteCommand? _stmtGetPackageExports;
    private SqliteParameter? _findByClassParam;
    private SqliteParameter? _findBySuperParam;
    private SqliteParameter? _findByOuterParam;
    private SqliteParameter? _findByTemplateParam;
    private SqliteParameter? _findByPropKeyParam;
    private SqliteParameter? _getPackageExportsParam;

    public int PackageCount => _packagePaths.Length;
    public int ExportCount => _exportCount;
    public int PropertyCount => _propertyCount;
    public IReadOnlyCollection<string> PackagePaths => _packagePaths;

    public ExportIndex(ILogger<ExportIndex> logger, string cacheDirectory)
    {
        _logger = logger;
        _cacheDirectory = cacheDirectory;
        _dbPath = Path.Combine(cacheDirectory, "export-index.db");
    }

    /// <summary>
    /// Loads the index from <c>export-index.db</c> if its stored hash matches the pak files,
    /// otherwise rebuilds from the provider.
    /// </summary>
    public async Task InitializeAsync(DefaultFileProvider provider)
    {
        await Task.Yield();

        string expectedHash = ComputePakHash(provider);
        Directory.CreateDirectory(_cacheDirectory);

        // Best-effort cleanup of legacy json.gz cache from before SQLite
        DeleteIfExists(Path.Combine(_cacheDirectory, "export-index.json.gz"));
        DeleteIfExists(Path.Combine(_cacheDirectory, "export-index.hash"));

        bool needRebuild = true;
        if (File.Exists(_dbPath))
        {
            try
            {
                string? cachedHash = TryReadStoredHash(_dbPath);
                if (cachedHash == expectedHash)
                {
                    _logger.LogInformation("Loading export index from cache: {Path}", _dbPath);
                    needRebuild = false;
                }
                else
                {
                    _logger.LogInformation("Pak hash mismatch, rebuilding index");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read cached index, rebuilding");
            }
        }

        if (needRebuild)
        {
            Build(provider, expectedHash);
        }

        OpenReadOnly();
        LoadPackageList();

        _logger.LogInformation("Index ready: {Packages} packages, {Exports} exports",
            PackageCount, ExportCount);
    }

    public ExportEntry[]? GetPackageExports(string packagePath)
    {
        if (_conn == null) throw new InvalidOperationException("Index not initialized");
        if (!_packageIdByPath.TryGetValue(packagePath, out long pkgId)) return null;

        if (_stmtGetPackageExports == null)
        {
            _stmtGetPackageExports = _conn.CreateCommand();
            _stmtGetPackageExports.CommandText = """
                SELECT e.export_index,
                       sn.text, sc.text, ss.text, so.text, st.text
                FROM exports e
                LEFT JOIN strings sn ON sn.id = e.name_id
                LEFT JOIN strings sc ON sc.id = e.class_id
                LEFT JOIN strings ss ON ss.id = e.super_id
                LEFT JOIN strings so ON so.id = e.outer_id
                LEFT JOIN strings st ON st.id = e.template_id
                WHERE e.package_id = $pid
                ORDER BY e.export_index
                """;
            _getPackageExportsParam = _stmtGetPackageExports.CreateParameter();
            _getPackageExportsParam.ParameterName = "$pid";
            _stmtGetPackageExports.Parameters.Add(_getPackageExportsParam);
            _stmtGetPackageExports.Prepare();
        }

        _getPackageExportsParam!.Value = pkgId;
        List<ExportEntry> entries = [];
        using SqliteDataReader rdr = _stmtGetPackageExports.ExecuteReader();
        int expectedIndex = 0;
        while (rdr.Read())
        {
            int idx = rdr.GetInt32(0);
            // Pad with empty entries for any gaps (shouldn't happen, but defensive)
            while (entries.Count < idx) entries.Add(new ExportEntry(null, null, null, null, null));
            entries.Add(new ExportEntry(
                rdr.IsDBNull(1) ? null : rdr.GetString(1),
                rdr.IsDBNull(2) ? null : rdr.GetString(2),
                rdr.IsDBNull(3) ? null : rdr.GetString(3),
                rdr.IsDBNull(4) ? null : rdr.GetString(4),
                rdr.IsDBNull(5) ? null : rdr.GetString(5)));
            expectedIndex = idx + 1;
        }
        _ = expectedIndex;
        return entries.ToArray();
    }

    public IReadOnlyList<ExportRef> FindByClass(string className)
        => FindBy(ref _stmtFindByClass, ref _findByClassParam, "class_id", className);

    public IReadOnlyList<ExportRef> FindBySuper(string superName)
        => FindBy(ref _stmtFindBySuper, ref _findBySuperParam, "super_id", superName);

    public IReadOnlyList<ExportRef> FindByOuter(string outerName)
        => FindBy(ref _stmtFindByOuter, ref _findByOuterParam, "outer_id", outerName);

    public IReadOnlyList<ExportRef> FindByTemplate(string templateName)
        => FindBy(ref _stmtFindByTemplate, ref _findByTemplateParam, "template_id", templateName);

    public IReadOnlyList<ExportRef> FindByPropertyKey(string propertyKey)
    {
        if (_conn == null) throw new InvalidOperationException("Index not initialized");

        if (_stmtFindByPropKey == null)
        {
            _stmtFindByPropKey = _conn.CreateCommand();
            _stmtFindByPropKey.CommandText = """
                SELECT e.package_id, e.export_index
                FROM properties p
                JOIN exports e ON e.id = p.export_id
                WHERE p.key_id = (SELECT id FROM strings WHERE text = $t)
                """;
            _findByPropKeyParam = _stmtFindByPropKey.CreateParameter();
            _findByPropKeyParam.ParameterName = "$t";
            _stmtFindByPropKey.Parameters.Add(_findByPropKeyParam);
            _stmtFindByPropKey.Prepare();
        }

        _findByPropKeyParam!.Value = propertyKey;
        List<ExportRef> result = [];
        using SqliteDataReader rdr = _stmtFindByPropKey.ExecuteReader();
        while (rdr.Read())
        {
            long pkgId = rdr.GetInt64(0);
            int exportIndex = rdr.GetInt32(1);
            if (pkgId > 0 && pkgId < _packagePathById.Length)
                result.Add(new ExportRef(_packagePathById[(int)pkgId], exportIndex));
        }
        return result;
    }

    public IReadOnlyCollection<string> GetClassNames()    => GetDistinctStringsForColumn("class_id");
    public IReadOnlyCollection<string> GetSuperNames()    => GetDistinctStringsForColumn("super_id");
    public IReadOnlyCollection<string> GetOuterNames()    => GetDistinctStringsForColumn("outer_id");
    public IReadOnlyCollection<string> GetTemplateNames() => GetDistinctStringsForColumn("template_id");

    public IReadOnlyCollection<string> GetPropertyKeys()
    {
        if (_conn == null) throw new InvalidOperationException("Index not initialized");

        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.text
            FROM strings s
            WHERE s.id IN (SELECT DISTINCT key_id FROM properties)
            """;
        List<string> result = [];
        using SqliteDataReader rdr = cmd.ExecuteReader();
        while (rdr.Read()) result.Add(rdr.GetString(0));
        return result;
    }

    public void Dispose()
    {
        _stmtFindByClass?.Dispose();
        _stmtFindBySuper?.Dispose();
        _stmtFindByOuter?.Dispose();
        _stmtFindByTemplate?.Dispose();
        _stmtFindByPropKey?.Dispose();
        _stmtGetPackageExports?.Dispose();
        _conn?.Dispose();
        _conn = null;
    }

    // ─────────────────────────────────────────────────────────────── build

    private void Build(DefaultFileProvider provider, string expectedHash)
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);

        Stopwatch swTotal = Stopwatch.StartNew();

        string[] packageFiles = provider.Files.Keys
            .Where(IsPackageFile)
            .ToArray();

        _logger.LogInformation("Building export index for {Count} package files -> {Path}",
            packageFiles.Length, _dbPath);

        using SqliteConnection conn = new($"Data Source={_dbPath}");
        conn.Open();

        ExecNonQuery(conn, """
            PRAGMA journal_mode = MEMORY;
            PRAGMA synchronous = OFF;
            PRAGMA temp_store = MEMORY;
            PRAGMA cache_size = -200000;
        """);
        ExecNonQuery(conn, Schema);

        using SqliteTransaction tx = conn.BeginTransaction();

        // In-memory string pool — flushed to the strings table at commit.
        // OrdinalIgnoreCase matches the previous Dictionary<string, List<ExportRef>> semantics.
        Dictionary<string, long> stringIds = new(StringComparer.OrdinalIgnoreCase);
        long nextStringId = 1;

        long InternString(string? s)
        {
            if (s == null) return 0; // 0 == NULL sentinel; stored as NULL FK
            if (stringIds.TryGetValue(s, out long id)) return id;
            id = nextStringId++;
            stringIds[s] = id;
            return id;
        }

        SqliteCommand insertPkg = conn.CreateCommand();
        insertPkg.Transaction = tx;
        insertPkg.CommandText = "INSERT INTO packages(path) VALUES($p); SELECT last_insert_rowid();";
        SqliteParameter p_pkgPath = AddParam(insertPkg, "$p");

        SqliteCommand insertExp = conn.CreateCommand();
        insertExp.Transaction = tx;
        insertExp.CommandText = """
            INSERT INTO exports(package_id, export_index, name_id, class_id, super_id, outer_id, template_id)
            VALUES($pid, $idx, $n, $c, $s, $o, $t);
            SELECT last_insert_rowid();
        """;
        SqliteParameter p_expPid = AddParam(insertExp, "$pid");
        SqliteParameter p_expIdx = AddParam(insertExp, "$idx");
        SqliteParameter p_expN = AddParam(insertExp, "$n");
        SqliteParameter p_expC = AddParam(insertExp, "$c");
        SqliteParameter p_expS = AddParam(insertExp, "$s");
        SqliteParameter p_expO = AddParam(insertExp, "$o");
        SqliteParameter p_expT = AddParam(insertExp, "$t");

        SqliteCommand insertProp = conn.CreateCommand();
        insertProp.Transaction = tx;
        insertProp.CommandText = "INSERT OR IGNORE INTO properties(export_id, key_id, type_id) VALUES($eid, $k, $t);";
        SqliteParameter p_propEid = AddParam(insertProp, "$eid");
        SqliteParameter p_propK = AddParam(insertProp, "$k");
        SqliteParameter p_propT = AddParam(insertProp, "$t");

        static object IdOrNull(long id) => id == 0 ? DBNull.Value : id;

        int processed = 0, errors = 0, exportCount = 0;
        Stopwatch swWalk = Stopwatch.StartNew();

        foreach (string filePath in packageFiles)
        {
            processed++;
            try
            {
                IPackage package = provider.LoadPackage(filePath);
                if (package == null) continue;

                p_pkgPath.Value = package.Name;
                long pkgId = (long)insertPkg.ExecuteScalar()!;

                Lazy<UObject>[] lazyExports = package.ExportsLazy;
                for (int i = 0; i < lazyExports.Length; i++)
                {
                    UObject export;
                    string? name, className, superName, outerName, templateName;
                    try
                    {
                        export = lazyExports[i].Value;
                        name = export.Name;
                        className = export.Class?.Name;
                        superName = export.Super?.Name.ToString();
                        outerName = export.Outer?.Name;
                        templateName = export.Template?.Name.ToString();
                    }
                    catch
                    {
                        errors++;
                        // Insert a null-filled placeholder so export_index stays aligned
                        p_expPid.Value = pkgId;
                        p_expIdx.Value = i;
                        p_expN.Value = p_expC.Value = p_expS.Value = p_expO.Value = p_expT.Value = DBNull.Value;
                        insertExp.ExecuteScalar();
                        exportCount++;
                        continue;
                    }

                    p_expPid.Value = pkgId;
                    p_expIdx.Value = i;
                    p_expN.Value = IdOrNull(InternString(name));
                    p_expC.Value = IdOrNull(InternString(className));
                    p_expS.Value = IdOrNull(InternString(superName));
                    p_expO.Value = IdOrNull(InternString(outerName));
                    p_expT.Value = IdOrNull(InternString(templateName));
                    long expId = (long)insertExp.ExecuteScalar()!;
                    exportCount++;

                    foreach (FPropertyTag tag in export.Properties)
                    {
                        string key = tag.Name.Text;
                        string type = tag.PropertyType.Text;

                        p_propEid.Value = expId;
                        p_propK.Value = InternString(key);
                        p_propT.Value = InternString(type);
                        insertProp.ExecuteNonQuery();
                    }
                }
            }
            catch
            {
                errors++;
            }

            if (processed % 5000 == 0)
            {
                _logger.LogInformation("Indexed {Processed}/{Total} packages ({Errors} errors, {Exports} exports, {Strings} strings)",
                    processed, packageFiles.Length, errors, exportCount, stringIds.Count);
            }
        }
        swWalk.Stop();

        // Flush the string pool. Insert in id order so packages.id (which arrives from auto-increment)
        // and strings.id are both contiguous starting at 1.
        _logger.LogInformation("Flushing {Count} interned strings", stringIds.Count);
        SqliteCommand insertStr = conn.CreateCommand();
        insertStr.Transaction = tx;
        insertStr.CommandText = "INSERT INTO strings(id, text) VALUES($i, $t);";
        SqliteParameter p_strId = AddParam(insertStr, "$i");
        SqliteParameter p_strText = AddParam(insertStr, "$t");

        foreach (KeyValuePair<string, long> kvp in stringIds.OrderBy(k => k.Value))
        {
            p_strId.Value = kvp.Value;
            p_strText.Value = kvp.Key;
            insertStr.ExecuteNonQuery();
        }

        // Persist the validation hash inside the DB.
        ExecNonQuery(conn, $"INSERT INTO meta(key, value) VALUES('pak_hash', '{expectedHash}');", tx);

        Stopwatch swCommit = Stopwatch.StartNew();
        tx.Commit();
        swCommit.Stop();

        _logger.LogInformation("Building indices...");
        Stopwatch swIndex = Stopwatch.StartNew();
        ExecNonQuery(conn, Indices);
        ExecNonQuery(conn, "ANALYZE;");
        swIndex.Stop();

        conn.Close();
        SqliteConnection.ClearAllPools();

        swTotal.Stop();
        long dbBytes = new FileInfo(_dbPath).Length;
        _logger.LogInformation(
            "Index build complete: {Exports} exports, {Errors} errors, {Strings} strings, " +
            "{Size:F1} MiB, walk {Walk:F1}s, commit {Commit:F1}s, index {Idx:F1}s, total {Total:F1}s",
            exportCount, errors, stringIds.Count,
            dbBytes / (1024.0 * 1024.0),
            swWalk.Elapsed.TotalSeconds, swCommit.Elapsed.TotalSeconds,
            swIndex.Elapsed.TotalSeconds, swTotal.Elapsed.TotalSeconds);
    }

    // ─────────────────────────────────────────────────────────── open / load

    private void OpenReadOnly()
    {
        _conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
        _conn.Open();
        ExecNonQuery(_conn, """
            PRAGMA query_only = ON;
            PRAGMA cache_size = -200000;
            PRAGMA temp_store = MEMORY;
        """);
    }

    private void LoadPackageList()
    {
        if (_conn == null) throw new InvalidOperationException();

        // Stream packages in id order so we can build a flat id→path array (1-based ids).
        List<string> paths = [];
        Dictionary<string, long> idByPath = new(StringComparer.OrdinalIgnoreCase);
        List<string> pathById = [string.Empty]; // index 0 unused (ids start at 1)

        using (SqliteCommand cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, path FROM packages ORDER BY id";
            using SqliteDataReader rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                long id = rdr.GetInt64(0);
                string path = rdr.GetString(1);
                paths.Add(path);
                idByPath[path] = id;
                while (pathById.Count <= id) pathById.Add(string.Empty);
                pathById[(int)id] = path;
            }
        }

        _packagePaths = paths.ToArray();
        _packageIdByPath = idByPath;
        _packagePathById = pathById.ToArray();

        using (SqliteCommand cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM exports";
            _exportCount = (int)(long)cmd.ExecuteScalar()!;
        }

        using (SqliteCommand cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM properties";
            _propertyCount = (int)(long)cmd.ExecuteScalar()!;
        }
    }

    private string? TryReadStoredHash(string dbPath)
    {
        using SqliteConnection conn = new($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = 'pak_hash'";
        return cmd.ExecuteScalar() as string;
    }

    // ─────────────────────────────────────────────────────────── query helpers

    private IReadOnlyList<ExportRef> FindBy(
        ref SqliteCommand? stmt, ref SqliteParameter? param, string columnId, string text)
    {
        if (_conn == null) throw new InvalidOperationException("Index not initialized");

        if (stmt == null)
        {
            stmt = _conn.CreateCommand();
            stmt.CommandText = $"""
                SELECT e.package_id, e.export_index
                FROM exports e
                WHERE e.{columnId} = (SELECT id FROM strings WHERE text = $t)
                """;
            param = stmt.CreateParameter();
            param.ParameterName = "$t";
            stmt.Parameters.Add(param);
            stmt.Prepare();
        }

        param!.Value = text;
        List<ExportRef> result = [];
        using SqliteDataReader rdr = stmt.ExecuteReader();
        while (rdr.Read())
        {
            long pkgId = rdr.GetInt64(0);
            int exportIndex = rdr.GetInt32(1);
            if (pkgId > 0 && pkgId < _packagePathById.Length)
                result.Add(new ExportRef(_packagePathById[(int)pkgId], exportIndex));
        }
        return result;
    }

    private IReadOnlyCollection<string> GetDistinctStringsForColumn(string columnId)
    {
        if (_conn == null) throw new InvalidOperationException("Index not initialized");

        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT s.text
            FROM strings s
            WHERE s.id IN (SELECT DISTINCT {columnId} FROM exports WHERE {columnId} IS NOT NULL)
            """;
        List<string> result = [];
        using SqliteDataReader rdr = cmd.ExecuteReader();
        while (rdr.Read()) result.Add(rdr.GetString(0));
        return result;
    }

    // ─────────────────────────────────────────────────────────── infrastructure

    private static string ComputePakHash(DefaultFileProvider provider)
    {
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

    private static void ExecNonQuery(SqliteConnection conn, string sql, SqliteTransaction? tx = null)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        if (tx != null) cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static SqliteParameter AddParam(SqliteCommand cmd, string name)
    {
        SqliteParameter p = cmd.CreateParameter();
        p.ParameterName = name;
        cmd.Parameters.Add(p);
        return p;
    }

    private static void DeleteIfExists(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }

    private const string Schema = """
        CREATE TABLE meta (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        ) WITHOUT ROWID;
        CREATE TABLE strings (
            id   INTEGER PRIMARY KEY,
            text TEXT NOT NULL UNIQUE COLLATE NOCASE
        );
        CREATE TABLE packages (
            id   INTEGER PRIMARY KEY,
            path TEXT NOT NULL UNIQUE
        );
        CREATE TABLE exports (
            id           INTEGER PRIMARY KEY,
            package_id   INTEGER NOT NULL,
            export_index INTEGER NOT NULL,
            name_id      INTEGER,
            class_id     INTEGER,
            super_id     INTEGER,
            outer_id     INTEGER,
            template_id  INTEGER
        );
        CREATE TABLE properties (
            export_id INTEGER NOT NULL,
            key_id    INTEGER NOT NULL,
            type_id   INTEGER NOT NULL,
            PRIMARY KEY (export_id, key_id)
        ) WITHOUT ROWID;
    """;

    private const string Indices = """
        CREATE INDEX idx_exports_pkg         ON exports(package_id);
        CREATE INDEX idx_exports_class_id    ON exports(class_id);
        CREATE INDEX idx_exports_super_id    ON exports(super_id);
        CREATE INDEX idx_exports_outer_id    ON exports(outer_id);
        CREATE INDEX idx_exports_template_id ON exports(template_id);
        CREATE INDEX idx_exports_name_id     ON exports(name_id);
        CREATE INDEX idx_properties_key_id   ON properties(key_id);
    """;
}

public record struct ExportEntry(string? Name, string? Class, string? Super, string? Outer, string? Template);
public record struct ExportRef(string PackagePath, int ExportIndex);

