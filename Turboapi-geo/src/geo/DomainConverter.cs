using Turboapi_geo.domain.events;
using Turboapi_geo.domain.value;

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
            nameof(LocationDisplayInformationChanged) => DeserializeLocationInformationChanged(rootElement),
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
            case LocationDisplayInformationChanged locationDisplayInformationChanged:
                WriteLocationDisplayChanged(writer, locationDisplayInformationChanged);
                break;
        }

        writer.WriteEndObject();
    }

    private LocationCreated DeserializeLocationCreated(JsonElement element, JsonSerializerOptions options)
    {
        var locationId = element.GetProperty("locationId").GetGuid();
        var ownerId = element.GetProperty("ownerId").GetString()!;
        var geometryElement = element.GetProperty("geometry");
        var displayElement = element.GetProperty("displayInformation");

        var geometry = JsonSerializer.Deserialize<Point>(geometryElement, options);
        var display = JsonSerializer.Deserialize<DisplayInformation>(displayElement, options);

        return new LocationCreated(locationId, ownerId, geometry, display);
    }

    private LocationPositionChanged DeserializeLocationPositionChanged(JsonElement element,  JsonSerializerOptions options)
    {
        var locationId = element.GetProperty("locationId").GetGuid();
        var geometryElement = element.GetProperty("geometry");
        var geometry = JsonSerializer.Deserialize<Point>(geometryElement, options);
        
        return new LocationPositionChanged(locationId, geometry);
    }
    
    private LocationDisplayInformationChanged DeserializeLocationInformationChanged(JsonElement element)
    {
        var locationId = element.GetProperty("locationId").GetGuid();
        var name = element.GetProperty("name").GetString() ?? "";
        var description = element.GetProperty("description").GetString() ?? "";
        var icon = element.GetProperty("icon").GetString() ?? "";
        
        return new LocationDisplayInformationChanged(locationId, name, description, icon);
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
    
    private void WriteLocationDisplayChanged(Utf8JsonWriter writer, LocationDisplayInformationChanged value)
    {
        writer.WriteString("locationId", value.LocationId);
        writer.WriteString("name", value.Name);
        writer.WriteString("description", value.Description);
        writer.WriteString("icon", value.Icon);
    }
    
    private void WriteLocationDeleted(Utf8JsonWriter writer, LocationDeleted value)
    {
        writer.WriteString("locationId", value.LocationId);
        writer.WriteString("ownerId", value.OwnerId);
    }
}