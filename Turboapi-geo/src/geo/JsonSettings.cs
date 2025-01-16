using System.Text.Json;
using NetTopologySuite.IO.Converters;


namespace Turboapi_geo.geo;

public static class JsonConfig
{
    
    public static JsonSerializerOptions CreateDefault()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new GeometryJsonConverter() }    
        };
        
        return options;
    }
}