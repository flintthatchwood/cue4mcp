using System.ComponentModel;

using CUE4Mcp.GameFiles;

using CUE4Parse.FileProvider;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ModelContextProtocol.Server;

using Newtonsoft.Json;

namespace CUE4Mcp.Tools;

[McpServerToolType]
public class ExportFileListTool
{
    private readonly IFileProvider _fileProvider;
    private readonly ILogger<ExportFileListTool> _logger;
    private readonly string _outputDirectory;

    public ExportFileListTool(ILogger<ExportFileListTool> logger, IFileProvider fileProvider, IOptions<FileProviderOptions> options)
    {
        _fileProvider = fileProvider;
        _logger = logger;
        _outputDirectory = options.Value.OutputDirectory;
    }

    [McpServerTool(Name = "cue4-export-file-list")]
    [Description("Export a complete list of all files to disk as JSON. Writes to OutputDirectory/all.json.")]
    public async Task<object> ExportFileList()
    {
        try
        {
            _logger.LogInformation("Exporting complete file list to disk");

            // Get all files
            var allFiles = _fileProvider.Files.Keys.ToArray();

            // Ensure output directory exists
            if (!Directory.Exists(_outputDirectory))
            {
                Directory.CreateDirectory(_outputDirectory);
            }

            var outputPath = Path.Combine(_outputDirectory, "all.json");

            // Write to file
            var json = JsonConvert.SerializeObject(new
            {
                success = true,
                totalFileCount = allFiles.Length,
                exportedAt = DateTime.UtcNow,
                files = allFiles
            }, Formatting.Indented);

            await File.WriteAllTextAsync(outputPath, json);

            _logger.LogInformation("Successfully exported {Count} files to {Path}", allFiles.Length, outputPath);

            return new
            {
                success = true,
                filePath = outputPath,
                fileCount = allFiles.Length,
                message = $"Exported {allFiles.Length} files to {outputPath}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting file list");
            return new { error = ex.Message, stackTrace = ex.StackTrace };
        }
    }
}
