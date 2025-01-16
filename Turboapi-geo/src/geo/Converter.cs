using System.Text.Json;
using System.Text.Json.Serialization;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

public class GeometryJsonConverter : JsonConverter<Point>
{
    public override Point Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var jsonDoc = JsonDocument.ParseValue(ref reader);
        var jsonString = jsonDoc.RootElement.GetRawText();
        var geoJsonReader = new GeoJsonReader();
        return geoJsonReader.Read<Point>(jsonString);
    }

    public override void Write(Utf8JsonWriter writer, Point value, JsonSerializerOptions options)
    {
        var geoJsonWriter = new GeoJsonWriter();
        var json = geoJsonWriter.Write(value);
        using var jsonDoc = JsonDocument.Parse(json);
        jsonDoc.RootElement.WriteTo(writer);
    }
}