using ModelContextProtocol.Server;

using System.ComponentModel;

namespace CUE4Mcp.Resources;

[McpServerResourceType]
public class GuidelinesResource
{
    private static string GuidelinesContent = string.Empty;

    [McpServerResource(Name = "guidelines.md")]
    [Description("CUE4Parse server guidelines and usage documentation")]
    public string GetGuidelines()
    {
        if(!string.IsNullOrEmpty(GuidelinesContent))
        {
            return GuidelinesContent;
        }

        // return guidelines from an embedded resource
        var assembly = typeof(GuidelinesResource).Assembly;
        using var stream = assembly.GetManifestResourceStream("cue4mcp.guidelines.md");
        if (stream == null)
        {
            GuidelinesContent = "Guidelines document not found.";
        }
        else
        {
            using var reader = new StreamReader(stream);
            GuidelinesContent = reader.ReadToEnd();
        }

        return GuidelinesContent;
    }
}
