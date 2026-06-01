using ModelContextProtocol.Server;

using System.ComponentModel;
using System.Reflection;

namespace CUE4Mcp.Resources;

[McpServerResourceType]
public class GuidelinesResource
{
    private static string GuidelinesContent = string.Empty;

    [McpServerResource(Name = "guidelines.md")]
    [Description("CUE4Parse server guidelines and usage documentation")]
    public string GetGuidelines()
    {
        if (!string.IsNullOrEmpty(GuidelinesContent))
        {
            return GuidelinesContent;
        }

        // return guidelines from an embedded resource
        Assembly assembly = typeof(GuidelinesResource).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream("cue4mcp.guidelines.md");
        if (stream == null)
        {
            GuidelinesContent = "Guidelines document not found.";
        }
        else
        {
            using StreamReader reader = new(stream);
            GuidelinesContent = reader.ReadToEnd();
        }

        return GuidelinesContent;
    }
}
