namespace CUE4Mcp.Domain;

/// <summary>
/// Abstraction over the in-memory export index to allow DI mocking in tests.
/// </summary>
public interface IExportIndex
{
    int PackageCount { get; }
    int ExportCount { get; }

    /// <summary>All package paths known to the index.</summary>
    IReadOnlyCollection<string> PackagePaths { get; }

    ExportEntry[]? GetPackageExports(string packagePath);

    IReadOnlyList<ExportRef> FindByClass(string className);
    IReadOnlyList<ExportRef> FindBySuper(string superName);
    IReadOnlyList<ExportRef> FindByOuter(string outerName);
    IReadOnlyList<ExportRef> FindByTemplate(string templateName);

    /// <summary>Find all exports that declare a property with the given key name.</summary>
    IReadOnlyList<ExportRef> FindByPropertyKey(string propertyKey);

    IReadOnlyCollection<string> GetClassNames();
    IReadOnlyCollection<string> GetSuperNames();
    IReadOnlyCollection<string> GetOuterNames();
    IReadOnlyCollection<string> GetTemplateNames();

    /// <summary>Distinct property keys observed across all exports.</summary>
    IReadOnlyCollection<string> GetPropertyKeys();

    /// <summary>Total number of property tags indexed across all exports.</summary>
    int PropertyCount { get; }
}
