using CUE4Mcp.Domain;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;

namespace cue4mcp.FunctionalTests;

[TestClass]
public class SearchExportsTests
{
    private FileService FileService => FileServiceFixture.FileService;

    [TestMethod]
    public void ListPackages_WithPattern_ReturnsFilteredResults()
    {
        IReadOnlyList<string> result = FileService.ListPackages(".*Dinos/Achatina.*");

        Assert.IsNotEmpty(result, "Should find Achatina packages");
        Assert.IsTrue(result.All(p => p.Contains("Achatina", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void ListPackages_WithNoPattern_ReturnsAll()
    {
        IReadOnlyList<string> result = FileService.ListPackages();

        Assert.IsGreaterThan(1000, result.Count, $"Expected many packages, got {result.Count}");
    }

    [TestMethod]
    public void GetPackage_ValidPackage_ReturnsExports()
    {
        IReadOnlyList<string> packages = FileService.ListPackages(".*Dinos/Achatina.*");
        Assert.IsNotEmpty(packages);

        IPackage package = FileService.GetPackage(packages[0]);

        Assert.IsNotNull(package);
        Assert.IsNotEmpty(package.ExportsLazy, "Package should have exports");
    }

    [TestMethod]
    public void GetExport_ValidIndex_ReturnsExport()
    {
        IReadOnlyList<string> packages = FileService.ListPackages(".*Dinos/Achatina.*");
        IPackage package = FileService.GetPackage(packages[0]);
        int exportCount = package.ExportsLazy.Length;

        UObject export = FileService.GetExport(packages[0], 0);

        Assert.IsNotNull(export);
        Assert.IsNotNull(export.Name);
    }

    [TestMethod]
    public void SearchExports_ByFieldName_ReturnsResults()
    {
        List<SearchResult> results = FileService.SearchExports(
            keyPattern: "DinoNameTag",
            packagePattern: ".*Dinos/Achatina.*").ToList();

        Assert.IsNotEmpty(results, "Should find exports with DinoNameTag field");
    }

    [TestMethod]
    public async Task ExportPackagesToDisk_WithPattern_WritesFiles()
    {
        IReadOnlyList<string> result = await FileService.ExportPackagesToDiskAsync(".*Dinos/Achatina/Achatina_Character_BP$");

        Assert.IsNotEmpty(result, "Should export at least one package");
        Assert.IsTrue(result.All(File.Exists), "All exported files should exist on disk");
    }
}