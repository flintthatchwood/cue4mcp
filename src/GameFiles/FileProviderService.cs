using CUE4Parse.Compression;
using CUE4Parse.FileProvider;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CUE4Mcp.GameFiles;

internal class FileProviderService
{
    private readonly FileProviderOptions _options;
    private readonly ILogger<FileProviderService> _logger;
    private IFileProvider? _fileProvider;

    public FileProviderService(ILogger<FileProviderService> logger, IOptions<FileProviderOptions> options)
    {
        _options = options.Value;
        _logger = logger;
    }

    internal IFileProvider GetFileProvider()
    {
        if (_fileProvider == null)
        {
            throw new InvalidOperationException("FileProviderService not initialized");
        }

        return _fileProvider;
    }

    public async Task InitializeAsync()
    {
        var directory = _options.Directory;
        var gameString = _options.Game;
        var searchOptionString = _options.SearchOption;
        var aesKey = _options.AesKey;

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

        var searchOption = string.IsNullOrEmpty(searchOptionString) ? SearchOption.TopDirectoryOnly : Enum.Parse<SearchOption>(searchOptionString);

        _logger.LogInformation("Creating DefaultFileProvider for {Directory} with {Game}", directory, game);

        var fileProvider = new DefaultFileProvider(
            directory,
            searchOption,
            new CUE4Parse.UE4.Versions.VersionContainer(game),
            StringComparer.OrdinalIgnoreCase);

        fileProvider.Initialize();

        if (!string.IsNullOrWhiteSpace(aesKey))
        {
            var cleanKey = aesKey.Replace("0x", "").Replace("0X", "");
            fileProvider.SubmitKey(new CUE4Parse.UE4.Objects.Core.Misc.FGuid(), new CUE4Parse.Encryption.Aes.FAesKey(cleanKey));
            _logger.LogInformation("AES key submitted");
        }

        await fileProvider.MountAsync();
        _logger.LogInformation("Provider mounted with {Count} files", fileProvider.Files.Count);

        _fileProvider = fileProvider;
    }

    // Initialize Oodle compression if available
    private async Task InitializeOodleAsync()
    {
        var nativeOodleFilename = Environment.OSVersion.Platform switch
        {
            PlatformID.Win32NT => "oo2core_9_win64.dll",
            PlatformID.Unix => "liboo2corelinux64.so.9",
            _ => throw new PlatformNotSupportedException("Unsupported OS for Oodle"),
        };

        var oodleLocation = Path.Join(AppContext.BaseDirectory, nativeOodleFilename);
        _logger.LogInformation("Initializing Oodle from {OodleLocation}", oodleLocation);

        if (!File.Exists(oodleLocation))
        {
            throw new PlatformNotSupportedException($"{nativeOodleFilename} not found.");
        }

        OodleHelper.Initialize(oodleLocation);
    }
}
