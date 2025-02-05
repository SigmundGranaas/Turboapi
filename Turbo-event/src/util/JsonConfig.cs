using System.Text.Json;
using System.Text.Json.Serialization;

namespace Turbo_event.util;

public static class JsonConfig
{
    
    public static JsonSerializerOptions CreateDefault(JsonConverter converter)
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { 
                converter
            }
        };
    }
}