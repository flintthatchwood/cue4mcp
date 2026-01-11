using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using CUE4Mcp.GameFiles;
using Microsoft.Extensions.Configuration;

namespace CUE4Mcp;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Add development configuration
        builder.Logging.AddConsole(consoleLogOptions => consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.Configure<FileProviderOptions>(builder.Configuration.GetSection("FileProvider"));
        builder.Services.AddSingleton<FileProviderService>();
        builder.Services.AddSingleton(sp => sp.GetRequiredService<FileProviderService>().GetFileProvider());

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly()
            .WithResourcesFromAssembly();

        var host = builder.Build();

        var fileProviderService = host.Services.GetRequiredService<FileProviderService>();

        await fileProviderService.InitializeAsync();
        
        await host.RunAsync();
    }
}
