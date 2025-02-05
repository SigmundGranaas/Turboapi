using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Turbo_event.kafka;

public class KafkaMessageProcessor<TEvent> where TEvent : class
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<KafkaMessageProcessor<TEvent>> _logger;

    public KafkaMessageProcessor(
        IServiceScopeFactory scopeFactory,
        JsonSerializerOptions jsonOptions,
        ILogger<KafkaMessageProcessor<TEvent>> logger)
    {
        _scopeFactory = scopeFactory;
        _jsonOptions = jsonOptions;
        _logger = logger;
    }

    public async Task ProcessMessageAsync(ConsumeResult<string, string> result, CancellationToken token)
    {
        using var scope = _scopeFactory.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IEventHandler<TEvent>>();
        
        var @event = JsonSerializer.Deserialize<TEvent>(result.Message.Value, _jsonOptions);
        await handler.HandleAsync(@event, token);
    }
}