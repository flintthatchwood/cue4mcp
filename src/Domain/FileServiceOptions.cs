namespace CUE4Mcp.Domain;

public class FileServiceOptions
{
    public string Directory { get; set; } = string.Empty;
    public string Game { get; set; } = "UE5_3";
    public string SearchOption { get; set; } = "TopDirectoryOnly";
    public string? AesKey { get; set; }
    public string OutputDirectory { get; set; } = ".work";
}
