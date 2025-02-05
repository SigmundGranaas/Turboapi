
public interface IEventTypeRegistry
{
    void RegisterEventType<TEvent>(string? name = null) where TEvent : Event;
    Type ResolveType(string typeName);
    string GetTypeName(Type type);
}

public class EventTypeRegistry : IEventTypeRegistry
{
    private readonly Dictionary<string, Type> _typeMap = new();
    private readonly Dictionary<Type, string> _nameMap = new();

    public void RegisterEventType<TEvent>(string? name = null) where TEvent : Event
    {
        var type = typeof(TEvent);
        var typeName = name ?? type.Name;
        _typeMap[typeName] = type;
        _nameMap[type] = typeName;
    }

    public Type ResolveType(string typeName) => 
        _typeMap.TryGetValue(typeName, out var type) 
            ? type 
            : throw new InvalidOperationException($"Unknown event type: {typeName}");

    public string GetTypeName(Type type) =>
        _nameMap.TryGetValue(type, out var name) 
            ? name 
            : throw new InvalidOperationException($"Unregistered event type: {type.Name}");
}