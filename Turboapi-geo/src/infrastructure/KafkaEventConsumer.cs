using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using Turboapi_geo.domain.events;
using Turboapi_geo.geo;
using Turboapi_geo.infrastructure;
using Turboapi.infrastructure;

public class KafkaLocationConsumer : BackgroundService 
{
    private readonly IConsumer<string, string> _consumer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IKafkaTopicInitializer _topicInitializer;
    private readonly string _topic;
    private readonly ILogger<KafkaLocationConsumer> _logger;
    private readonly CancellationTokenSource _stopConsumer;
    private volatile bool _isRunning;
    private Task _consumeTask;

    public KafkaLocationConsumer(
        IKafkaTopicInitializer topicInitializer,
        IOptions<KafkaSettings> settings,
        IServiceScopeFactory scopeFactory,
        ILogger<KafkaLocationConsumer> logger)
    {
        _topicInitializer = topicInitializer;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _stopConsumer = new CancellationTokenSource();
        
        ArgumentNullException.ThrowIfNull(settings?.Value);
        _topic = settings.Value.LocationEventsTopic;

        var config = new ConsumerConfig
        {
            BootstrapServers = settings.Value.BootstrapServers,
            GroupId = "location-read-model-updater",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnablePartitionEof = true,
            SessionTimeoutMs = 45000,
            MaxPollIntervalMs = 300000,
            HeartbeatIntervalMs = 3000,
            SecurityProtocol = SecurityProtocol.Plaintext,
            AllowAutoCreateTopics = true,
            EnableAutoOffsetStore = false
        };

        _consumer = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, e) => 
            {
                _logger.LogError("Kafka error: {Error}", e.Reason);
                if (e.IsFatal)
                {
                    _stopConsumer.Cancel();
                }
            })
            .Build();
    }
    private static readonly Dictionary<string, Type> EventTypes = new()
    {
        { nameof(LocationCreated), typeof(LocationCreated) },
        { nameof(LocationPositionChanged), typeof(LocationPositionChanged) },
        { nameof(LocationDeleted), typeof(LocationDeleted) }
    };

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _isRunning = true;
            _logger.LogInformation("Starting Kafka consumer for topic: {Topic}", _topic);
            
            await _topicInitializer.EnsureTopicExists(_topic);

            // Start the consume loop in a separate task
            _consumeTask = Task.Run(async () =>
            {
                try
                {
                    _consumer.Subscribe(_topic);

                    while (!stoppingToken.IsCancellationRequested && _isRunning)
                    {
                        try
                        {
                            var consumeResult = _consumer.Consume(TimeSpan.FromMilliseconds(100));
                            if (consumeResult == null) continue;

                            if (consumeResult.IsPartitionEOF)
                            {
                                _logger.LogDebug("Reached end of partition: {Partition}", consumeResult.Partition);
                                continue;
                            }

                            await ProcessMessage(consumeResult, stoppingToken);
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing message");
                            if (!stoppingToken.IsCancellationRequested)
                            {
                                await Task.Delay(1000, stoppingToken);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Fatal error in consumer loop");
                }
                finally
                {
                    await CleanupAsync();
                }
            }, stoppingToken);

            // Wait for the consume task to complete when the application is stopping
            await Task.WhenAny(_consumeTask, Task.Delay(Timeout.Infinite, stoppingToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ExecuteAsync");
        }
    }

    private async Task ProcessMessage(ConsumeResult<string, string> result, CancellationToken cancellationToken)
    {
        try
        {
            var message = result.Message;
            if (string.IsNullOrEmpty(message.Key) || string.IsNullOrEmpty(message.Value))
            {
                _logger.LogError("Received message with null/empty key or value");
                _consumer.Commit(result);
                return;
            }

            if (!EventTypes.TryGetValue(message.Key, out var eventType))
            {
                _logger.LogError("Unknown event type: {EventType}", message.Key);
                _consumer.Commit(result);
                return;
            }

            var domainEvent = (DomainEvent)JsonSerializer.Deserialize(message.Value, eventType, JsonConfig.CreateDefault())!;
            if (domainEvent == null)
            {
                _logger.LogError("Failed to deserialize event of type {EventType}", message.Key);
                _consumer.Commit(result);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var handlerType = typeof(ILocationEventHandler<>).MakeGenericType(eventType);
            var handler = scope.ServiceProvider.GetRequiredService(handlerType);

            await ((dynamic)handler).HandleAsync((dynamic)domainEvent, cancellationToken);
            
            // Store the offset before committing
            _consumer.StoreOffset(result);
            _consumer.Commit(result);
            
            _logger.LogInformation("Successfully processed and committed {EventType} at offset {Offset}", 
                eventType.Name, result.Offset.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message at offset {Offset}", result.Offset.Value);
            // Don't rethrow - let the consumer continue processing
        }
    }


    private async Task CleanupAsync()
    {
        try
        {
            _isRunning = false;
            var assignment = _consumer.Assignment;
            if (assignment?.Count > 0)
            {
                try
                {
                    _consumer.Commit();
                    _logger.LogInformation("Successfully committed offsets during shutdown");
                }
                catch (KafkaException ex) when (ex.Error.Code == ErrorCode.OffsetNotAvailable)
                {
                    _logger.LogInformation("No offsets to commit during shutdown");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error committing offsets during shutdown");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cleanup");
        }
        finally
        {
            try
            {
                _consumer.Close();
                _logger.LogInformation("Consumer closed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error closing consumer");
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _isRunning = false;
        _stopConsumer.Cancel();

        if (_consumeTask != null)
        {
            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await Task.WhenAny(_consumeTask, Task.Delay(Timeout.Infinite, timeoutCts.Token));
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Shutdown timeout reached");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during stop");
            }
        }

        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _consumer?.Dispose();
        _stopConsumer?.Dispose();
        base.Dispose();
    }
}

