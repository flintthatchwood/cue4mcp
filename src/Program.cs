using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using CUE4Mcp.Domain;
using Microsoft.Extensions.Configuration;

namespace CUE4Mcp;

internal class Program
{
    private static async Task Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        // Add development configuration
        builder.Logging.AddConsole(consoleLogOptions => consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.Configure<FileServiceOptions>(builder.Configuration.GetSection("FileProvider"));

        // Register FileService as singleton
        builder.Services.AddSingleton<FileService>();
        builder.Services.AddSingleton<ExportIndex>(sp =>
        {
            ILogger<ExportIndex> logger = sp.GetRequiredService<ILogger<ExportIndex>>();
            FileServiceOptions options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<FileServiceOptions>>().Value;
            string cacheDir = Path.Combine(options.OutputDirectory, "cache");
            return new ExportIndex(logger, cacheDir);
        });

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly()
            .WithResourcesFromAssembly();

        IHost host = builder.Build();

        // Initialize FileService
        FileService fileService = host.Services.GetRequiredService<FileService>();
        await fileService.InitializeAsync();

        // Initialize export index (loads from cache or builds from provider)
        ExportIndex exportIndex = host.Services.GetRequiredService<ExportIndex>();
        await exportIndex.InitializeAsync(fileService.Provider);

        await host.RunAsync();
    }
}
