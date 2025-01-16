using Turboapi_geo.domain.events;

namespace Turboapi_geo.geo;

using System.Text.Json;
using System.Text.Json.Serialization;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

public class DomainEventJsonConverter : JsonConverter<DomainEvent>
{
    private readonly GeometryJsonConverter _geometryConverter;
    
    public DomainEventJsonConverter()
    {
        _geometryConverter = new GeometryJsonConverter();
    }

    public override DomainEvent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected start of object");
        }

        using var jsonDoc = JsonDocument.ParseValue(ref reader);
        var rootElement = jsonDoc.RootElement;

        if (!rootElement.TryGetProperty("$type", out var typeProperty))
        {
            throw new JsonException("Missing $type discriminator");
        }

        var typeDiscriminator = typeProperty.GetString();
        return typeDiscriminator switch
        {
            nameof(LocationCreated) => DeserializeLocationCreated(rootElement, options),
            nameof(LocationPositionChanged) => DeserializeLocationPositionChanged(rootElement, options),
            nameof(LocationDeleted) => DeserializeLocationDeleted(rootElement),
            _ => throw new JsonException($"Unknown event type: {typeDiscriminator}")
        };
    }

    public override void Write(Utf8JsonWriter writer, DomainEvent value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        // Write common properties
        writer.WriteString("$type", value.GetType().Name);
        writer.WriteString("id", value.Id.ToString());
        writer.WriteString("occurredAt", value.OccurredAt.ToString("O"));

        // Write specific properties based on event type
        switch (value)
        {
            case LocationCreated locationCreated:
                WriteLocationCreated(writer, locationCreated);
                break;
            case LocationPositionChanged locationPositionChanged:
                WriteLocationPositionChanged(writer, locationPositionChanged);
                break;
            case LocationDeleted locationDeleted:
                WriteLocationDeleted(writer, locationDeleted);
                break;
        }

        writer.WriteEndObject();
    }

    private LocationCreated DeserializeLocationCreated(JsonElement element, JsonSerializerOptions options)
    {
        var locationId = element.GetProperty("locationId").GetGuid();
        var ownerId = element.GetProperty("ownerId").GetString()!;
        var geometryElement = element.GetProperty("geometry");
        var geometry = JsonSerializer.Deserialize<Point>(geometryElement, options);
        
        return new LocationCreated(locationId, ownerId, geometry);
    }

    private LocationPositionChanged DeserializeLocationPositionChanged(JsonElement element,  JsonSerializerOptions options)
    {
        var locationId = element.GetProperty("locationId").GetGuid();
        var geometryElement = element.GetProperty("geometry");
        var geometry = JsonSerializer.Deserialize<Point>(geometryElement, options);
        
        return new LocationPositionChanged(locationId, geometry);
    }

    private LocationDeleted DeserializeLocationDeleted(JsonElement element)
    {
        var locationId = element.GetProperty("locationId").GetGuid();
        var ownerId = element.GetProperty("ownerId").GetString()!;
        
        return new LocationDeleted(locationId, ownerId);
    }

    private void WriteLocationCreated(Utf8JsonWriter writer, LocationCreated value)
    {
        writer.WriteString("locationId", value.LocationId);
        writer.WriteString("ownerId", value.OwnerId);
        writer.WritePropertyName("geometry");
        _geometryConverter.Write(writer, value.Geometry, new JsonSerializerOptions());
    }

    private void WriteLocationPositionChanged(Utf8JsonWriter writer, LocationPositionChanged value)
    {
        writer.WriteString("locationId", value.LocationId);
        writer.WritePropertyName("geometry");
        _geometryConverter.Write(writer, value.Geometry, new JsonSerializerOptions());
    }

    private void WriteLocationDeleted(Utf8JsonWriter writer, LocationDeleted value)
    {
        writer.WriteString("locationId", value.LocationId);
        writer.WriteString("ownerId", value.OwnerId);
    }
}