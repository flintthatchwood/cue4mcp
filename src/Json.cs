using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CUE4Mcp;

public static class Json
{
    public static readonly JsonSerializerSettings DefaultSerializerSettings;
    public static readonly JsonSerializer DefaultSerializer;

    static Json()
    {
        DefaultSerializerSettings = new()
        {
            Formatting = Formatting.Indented,
            Converters = { new StringEnumConverter() }
        };
        DefaultSerializer = JsonSerializer.Create(DefaultSerializerSettings);
    }
}