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
        var builder = Host.CreateApplicationBuilder(args);

        // Add development configuration
        builder.Logging.AddConsole(consoleLogOptions => consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.Configure<FileServiceOptions>(builder.Configuration.GetSection("FileProvider"));

        // Register FileService as singleton
        builder.Services.AddSingleton<FileService>();

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly()
            .WithResourcesFromAssembly();

        var host = builder.Build();

        // Initialize FileService
        var fileService = host.Services.GetRequiredService<FileService>();
        await fileService.InitializeAsync();
        
        await host.RunAsync();
    }
}
