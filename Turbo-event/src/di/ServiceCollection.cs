using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Turbo_event.di;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEventSerialization(
        this IServiceCollection services,
        Action<IEventTypeRegistry> configure)
    {
        services.AddSingleton<IEventTypeRegistry, EventTypeRegistry>();
        services.AddSingleton<JsonConverter<Event>, EventJsonConverter>();
        
        var registry = new EventTypeRegistry();
        configure(registry);
        services.AddSingleton(registry);

        services.AddSingleton(sp =>
        {
            var registry = sp.GetRequiredService<IEventTypeRegistry>();
            return new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new EventJsonConverter(registry) }
            };
        });

        return services;
    }
}