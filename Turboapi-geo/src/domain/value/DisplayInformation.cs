namespace Turboapi_geo.domain.value;

public record DisplayInformation
{
    
    public static DisplayInformation CreateDefault()
    {
        return new DisplayInformation()
        {
            Name = "",
            Description = "",
            Icon = ""
        };
    }
    
    public static DisplayInformation of(string name, string? description, string? icon)
    {
        return new DisplayInformation()
        {
            Name = name,
            Description = description ?? "",
            Icon = icon ?? ""
        };
    }

    public required string Name { get; set; }
    public required string Icon { get; set; }
    public required string Description { get; set; }
}