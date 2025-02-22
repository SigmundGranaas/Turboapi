using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Turbo_event.kafka;

public class KafkaMessageProcessor<TEvent> where TEvent : class
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<KafkaMessageProcessor<TEvent>> _logger;
    private readonly Assembly _eventAssembly;

    public KafkaMessageProcessor(
        IServiceScopeFactory scopeFactory,
        JsonSerializerOptions jsonOptions,
        ILogger<KafkaMessageProcessor<TEvent>> logger)
    {
        _scopeFactory = scopeFactory;
        _jsonOptions = jsonOptions;
        _logger = logger;
        _eventAssembly = typeof(TEvent).Assembly;
    }

    public async Task ProcessMessageAsync(ConsumeResult<string, string> result, CancellationToken token)
    {
        using var scope = _scopeFactory.CreateScope();
        
        // Get the concrete event type based on the key
        var eventType = _eventAssembly.GetType($"{typeof(TEvent).Namespace}.{result.Message.Key}");
        if (eventType == null)
        {
            _logger.LogError("Unable to find event type: {EventType}", result.Message.Key);
            throw new InvalidOperationException($"Unknown event type: {result.Message.Key}");
        }

        // Deserialize to the concrete type
        var @event = JsonSerializer.Deserialize(result.Message.Value, eventType, _jsonOptions);
        
        // Get the generic handler interface type with the concrete event type
        var handlerType = typeof(IEventHandler<>).MakeGenericType(eventType);
        var handler = scope.ServiceProvider.GetRequiredService(handlerType);

        // Get and invoke the HandleAsync method
        var handleMethod = handlerType.GetMethod("HandleAsync");
        await (Task)handleMethod!.Invoke(handler, new[] { @event, token })!;
    }
}