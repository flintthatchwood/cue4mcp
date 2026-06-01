using System.Text.Json;

using CUE4Mcp.Domain;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace cue4mcp.FunctionalTests;

public sealed class TestSettings
{
    public FileServiceOptions FileProvider { get; set; } = new();

    private static readonly Lazy<TestSettings> _instance = new(Load);

    public static TestSettings Instance => _instance.Value;

    private static TestSettings Load()
    {
        // Look for testsettings.json next to the test assembly, then walk up
        string? dir = AppContext.BaseDirectory;
        string? path = null;

        while (dir != null)
        {
            string candidate = Path.Combine(dir, "testsettings.json");
            if (File.Exists(candidate))
            {
                path = candidate;
                break;
            }
            dir = Path.GetDirectoryName(dir);
        }

        if (path == null)
            throw new FileNotFoundException("testsettings.json not found. Copy testsettings.json to the test project directory.");

        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<TestSettings>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Failed to deserialize testsettings.json");
    }
}

[TestClass]
public sealed class FileServiceFixture
{
    private static FileService? _fileService;
    private static readonly SemaphoreSlim _initLock = new(1, 1);
    private static bool _initialized;

    public static FileService FileService => _fileService
        ?? throw new InvalidOperationException("FileServiceFixture has not been initialized. Ensure [AssemblyInitialize] ran.");

    [AssemblyInitialize]
    public static async Task Initialize(TestContext context)
    {
        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            TestSettings settings = TestSettings.Instance;
            ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
                builder.AddSimpleConsole(x => x.SingleLine = true).SetMinimumLevel(LogLevel.Information));

            ILogger<FileService> logger = loggerFactory.CreateLogger<FileService>();
            IOptions<FileServiceOptions> options = Options.Create(settings.FileProvider);

            _fileService = new FileService(logger, options);
            await _fileService.InitializeAsync();

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
