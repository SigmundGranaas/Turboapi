using System.Text.Json;
using Confluent.Kafka;
using GeoSpatial.Domain.Events;
using Microsoft.Extensions.Options;
using Turboapi_geo.domain.events;
using Turboapi_geo.geo;
using Turboapi.infrastructure;

public class KafkaEventSubscriber : IEventSubscriber, IHostedService, IDisposable
{
    private readonly Dictionary<Type, List<Func<DomainEvent, Task>>> _handlers = new();
    private readonly IConsumer<string, string> _consumer;
    private readonly ILogger<KafkaEventSubscriber> _logger;
    private readonly string _topic;
    private Task? _consumeTask;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    // Static mapping of event types for better performance and type safety
    private static readonly Dictionary<string, Type> EventTypes = new()
    {
        { typeof(LocationCreated).Name, typeof(LocationCreated) },
        { typeof(LocationPositionChanged).Name, typeof(LocationPositionChanged) },
        { typeof(LocationDeleted).Name, typeof(LocationDeleted) }
    };

    public KafkaEventSubscriber(
        IOptions<KafkaSettings> settings,
        ILogger<KafkaEventSubscriber> logger)
    {
        ArgumentNullException.ThrowIfNull(settings?.Value);
        
        _logger = logger;
        _topic = settings.Value.LocationEventsTopic;

        var config = new ConsumerConfig
        {
            BootstrapServers = settings.Value.BootstrapServers,
            GroupId = "location-read-model-updater",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            // Add some reasonable timeouts
            SessionTimeoutMs = 45000,
            MaxPollIntervalMs = 300000,
            HeartbeatIntervalMs = 3000
        };

        _consumer = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, e) => _logger.LogError("Kafka error: {Error}", e.Reason))
            .SetPartitionsAssignedHandler((_, partitions) =>
            {
                logger.LogInformation("Assigned partitions: {Partitions}", 
                    string.Join(", ", partitions.Select(p => p.Partition.Value)));
            })
            .SetPartitionsRevokedHandler((_, partitions) =>
            {
                logger.LogInformation("Revoked partitions: {Partitions}", 
                    string.Join(", ", partitions.Select(p => p.Partition.Value)));
            })
            .Build();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Kafka consumer for topic: {Topic}", _topic);
        _consumer.Subscribe(_topic);
        _consumeTask = Task.Run(ConsumeAsync, cancellationToken);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            _cts.Cancel();
            if (_consumeTask != null)
            {
                await Task.WhenAny(_consumeTask, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken));
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    private async Task ConsumeAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                var result = _consumer.Consume(TimeSpan.FromMilliseconds(100));
                if (result == null) continue;

                _logger.LogDebug("Received message with key: {Key}", result.Message.Key);
                
                var domainEvent = DeserializeEvent(result.Message);
                if (domainEvent == null) continue;

                var eventType = domainEvent.GetType();
                if (_handlers.TryGetValue(eventType, out var handlers))
                {
                    var success = true;
                    foreach (var handler in handlers)
                    {
                        try
                        {
                            await handler(domainEvent);
                        }
                        catch (Exception ex)
                        {
                            success = false;
                            _logger.LogError(ex, "Error handling event {EventType} with ID {EventId}", 
                                eventType.Name, domainEvent.Id);
                        }
                    }

                    if (success)
                    {
                        _consumer.Commit(result);
                        _logger.LogInformation("Successfully processed {EventType} with ID {EventId}", 
                            eventType.Name, domainEvent.Id);
                    }
                }
            }
            catch (ConsumeException ex)
            {
                _logger.LogError(ex, "Error consuming message");
                await Task.Delay(1000, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while consuming messages");
                await Task.Delay(1000, _cts.Token);
            }
        }
    }

    private DomainEvent? DeserializeEvent(Message<string, string> message)
    {
        try
        {
            if (string.IsNullOrEmpty(message.Key) || string.IsNullOrEmpty(message.Value))
            {
                _logger.LogError("Received message with null/empty key or value");
                return null;
            }

            if (!EventTypes.TryGetValue(message.Key, out var eventType))
            {
                _logger.LogError("Unknown event type: {EventType}", message.Key);
                return null;
            }
            

            var domainEvent = (DomainEvent)JsonSerializer.Deserialize(message.Value, eventType, JsonConfig.CreateDefault())!;
            
            if (domainEvent == null)
            {
                _logger.LogError("Failed to deserialize event of type {EventType}", message.Key);
                return null;
            }

            return domainEvent;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization error for event type {EventType}", message.Key);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error deserializing event");
            return null;
        }
    }

    public void Subscribe<T>(Func<T, Task> handler) where T : DomainEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        
        var eventType = typeof(T);
        
        if (!EventTypes.ContainsValue(eventType))
        {
            throw new ArgumentException($"Unsupported event type: {eventType.Name}");
        }

        lock (_handlers)
        {
            if (!_handlers.ContainsKey(eventType))
            {
                _handlers[eventType] = new List<Func<DomainEvent, Task>>();
            }

            _handlers[eventType].Add(@event => handler((T)@event));
        }
        
        _logger.LogInformation("Subscribed to event type: {EventType}", typeof(T).Name);
    }

    public void Unsubscribe<T>(Func<T, Task> handler) where T : DomainEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        
        var eventType = typeof(T);
        
        lock (_handlers)
        {
            if (_handlers.TryGetValue(eventType, out var handlers))
            {
                handlers.RemoveAll(h => h.Target == handler.Target && h.Method == handler.Method);
                
                if (!handlers.Any())
                {
                    _handlers.Remove(eventType);
                }
            }
        }
        
        _logger.LogInformation("Unsubscribed from event type: {EventType}", typeof(T).Name);
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _cts.Cancel();
        try
        {
            _consumer.Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing Kafka consumer");
        }
        _consumer.Dispose();
        _cts.Dispose();
        
        _disposed = true;
    }
}