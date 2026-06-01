namespace CUE4Mcp.Domain;

public class FileServiceOptions
{
    public string Directory { get; set; } = string.Empty;
    public string Game { get; set; } = "ARKSurvivalAscended";
    public string SearchOption { get; set; } = "TopDirectoryOnly";
    public string? AesKey { get; set; }
    public string? MappingsPath { get; set; }
    public string OutputDirectory { get; set; } = ".work";
}
