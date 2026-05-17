using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Turbo.Messaging.Nats;

public static class NatsServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="NatsMessageTransport"/> as the publish-side
    /// <see cref="IMessageTransport"/> and the host that consumes any
    /// subscriptions added via <see cref="AddNatsSubscriber{TEvent, THandler}"/>.
    /// </summary>
    public static IServiceCollection AddNatsMessaging(
        this IServiceCollection services,
        Action<NatsMessagingOptions> configure)
    {
        services.Configure(configure);
        services.TryAddSingleton<IMessageTransport, NatsMessageTransport>();
        services.AddHostedService<NatsSubscriberHost>();
        return services;
    }

    /// <summary>
    /// Registers a NATS JetStream subscription for one event type, routed
    /// to <typeparamref name="THandler"/> through a fresh DI scope per message.
    /// <typeparamref name="THandler"/> must declare a public
    /// <c>Task HandleAsync(TEvent, CancellationToken)</c> method.
    /// </summary>
    public static IServiceCollection AddNatsSubscriber<TEvent, THandler>(
        this IServiceCollection services,
        string subject,
        string durableName)
        where TEvent : class, IDomainEvent
        where THandler : class
    {
        services.TryAddScoped<THandler>();

        var method = typeof(THandler).GetMethod(
            "HandleAsync",
            [typeof(TEvent), typeof(CancellationToken)])
            ?? throw new InvalidOperationException(
                $"{typeof(THandler).Name} does not declare " +
                $"public Task HandleAsync({typeof(TEvent).Name}, CancellationToken)");

        services.AddSingleton(new NatsSubscriberRegistration
        {
            Subject = subject,
            DurableName = durableName,
            EventType = typeof(TEvent),
            Dispatcher = async (sp, data, ct) =>
            {
                var evt = JsonSerializer.Deserialize<TEvent>(data.Span)
                          ?? throw new InvalidOperationException(
                              $"NATS payload for subject {subject} deserialized to null for {typeof(TEvent).Name}");
                var handler = sp.GetRequiredService<THandler>();
                await (Task)method.Invoke(handler, [evt, ct])!;
            }
        });
        return services;
    }
}
