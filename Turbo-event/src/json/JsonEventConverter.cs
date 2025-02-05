using System.Text.Json;
using System.Text.Json.Serialization;

public class EventJsonConverter : JsonConverter<Event>
{
    private readonly IEventTypeRegistry _typeRegistry;

    public EventJsonConverter(IEventTypeRegistry typeRegistry)
    {
        _typeRegistry = typeRegistry;
    }

    public override Event? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("$type", out var typeElement))
            throw new JsonException("Missing $type discriminator");

        var typeName = typeElement.GetString()!;
        var concreteType = _typeRegistry.ResolveType(typeName);
        
        // Create new options without this converter to avoid recursion
        var eventOptions = new JsonSerializerOptions(options);
        eventOptions.Converters.Remove(this);
        
        return (Event)JsonSerializer.Deserialize(root.GetRawText(), concreteType, eventOptions)!;
    }

    public override void Write(Utf8JsonWriter writer, Event value, JsonSerializerOptions options)
    {
        var derivedJson = JsonSerializer.SerializeToDocument(value, value.GetType(), options);
        
        writer.WriteStartObject();
        writer.WriteString("$type", _typeRegistry.GetTypeName(value.GetType()));
        
        foreach (var property in derivedJson.RootElement.EnumerateObject())
        {
            if (property.Name != "$type")
            {
                property.WriteTo(writer);
            }
        }
        
        writer.WriteEndObject();
    }
}