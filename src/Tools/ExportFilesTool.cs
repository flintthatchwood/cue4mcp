using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;

using CUE4Mcp.GameFiles;

using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ModelContextProtocol.Server;

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace CUE4Mcp.Tools;

[McpServerToolType]
public class ExportFilesTool
{
    private readonly IFileProvider _fileProvider;
    private readonly ILogger<ExportFilesTool> _logger;
    private readonly string _outputDirectory;

    public ExportFilesTool(ILogger<ExportFilesTool> logger, IFileProvider fileProvider, IOptions<FileProviderOptions> options)
    {
        _fileProvider = fileProvider;
        _logger = logger;
        _outputDirectory = options.Value.OutputDirectory.Replace(@"\", "/");
    }

    [McpServerTool(Name = "cue4-export-files")]
    [Description("Export multiple files including all exports and their properties to disk as JSON. Writes to OutputDirectory/{filePath}.json.")]
    public async Task<object> ExportFiles(
        [Description("Regex filter for files to process (e.g., '.*Cosmetics.*Character.*'). Must use Linux-style path separators (/) in patterns.")]
        string filter)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                return new { error = "Path filter is required", code = -32602 };
            }

            _logger.LogInformation("Exporting files to disk: {Filter}", filter);

            var matchingFiles = _fileProvider.Files.Keys
                .Where(path => Regex.IsMatch(path, filter, RegexOptions.IgnoreCase))
                .ToArray();

            var exportedFiles = new List<string>();

            foreach(var file in matchingFiles.AsParallel())
            {
                exportedFiles.Add(await ExportFileAsync(file));
            }

            return new
            {
                success = true,
                message = $"Exported {exportedFiles.Count} files",
                filePaths = exportedFiles.Count < 10 ? exportedFiles : null,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting file");
            return new { error = ex.Message, stackTrace = ex.StackTrace };
        }
    }

    private async Task<string> ExportFileAsync(string path)
    {
        // Sanitize the path for use as a filename (path already uses forward slashes from file provider)
        var sanitizedPath = Regex.Replace(path, @"[^\w-\./]", "_");
        var outputPath = Path.Combine(_outputDirectory, Path.ChangeExtension(sanitizedPath, ".json"));

        if (File.Exists(outputPath))
        {
            _logger.LogInformation("File already exported, skipping: {Path}", outputPath);
            return outputPath;
        }

        // Try to load the package
        IPackage? package;
        try
        {
            package = _fileProvider.LoadPackage(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load package: {Path}", path);
            throw new Exception($"Failed to load package: {ex.Message}");
        }

        if (package == null)
        {
            throw new Exception($"Package not found: {path}");
        }

        // Ensure output directory exists
        var parentDirectory = Path.GetDirectoryName(outputPath)!;
        if (!Directory.Exists(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        // Build exports list with full export data
        var exports = package.GetExports().ToArray();

        // Write to file
        var json = JsonConvert.SerializeObject(new
        {
            package.Name,
            package.Summary,
            Exports = exports
        }, Formatting.Indented);

        await File.WriteAllTextAsync(outputPath, json);

        _logger.LogInformation("Successfully exported file {Path} with {ExportCount} exports to {OutputPath}", path, exports.Length, outputPath);

        return outputPath;
    }
}
