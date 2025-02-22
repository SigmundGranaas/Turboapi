using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using Turboapi_geo.domain.events;
using Turboapi_geo.geo;
using Turboapi_geo.infrastructure;
using Turboapi.infrastructure;

public class KafkaConsumerConfig<TEvent> where TEvent : DomainEvent
{
    public string Topic { get; set; } = null!;
    public string GroupId { get; set; } = null!;
}

public class GenericKafkaConsumer<TEvent> : BackgroundService where TEvent : DomainEvent
{
    private readonly IConsumer<string, string> _consumer;
    private readonly KafkaTopicInitializer _initializer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaConsumerConfig<TEvent> _config;
    private readonly ILogger<GenericKafkaConsumer<TEvent>> _logger;
    private readonly string _expectedEventType;
    private readonly CancellationTokenSource _stopConsumer;
    private volatile bool _isRunning;
    private Task _consumeTask;

    public GenericKafkaConsumer(
        IServiceScopeFactory scopeFactory,
        KafkaConsumerConfig<TEvent> config,
        IOptions<KafkaSettings> settings,
        ILogger<GenericKafkaConsumer<TEvent>> logger
       )
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
        _expectedEventType = typeof(TEvent).Name;
        _stopConsumer = new CancellationTokenSource();
        _initializer = new KafkaTopicInitializer(settings);

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = settings.Value.BootstrapServers,
            GroupId = config.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnablePartitionEof = true,
            SessionTimeoutMs = 45000,
            MaxPollIntervalMs = 300000,
            HeartbeatIntervalMs = 3000
        };

        _consumer = new ConsumerBuilder<string, string>(consumerConfig)
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _isRunning = true;
            _logger.LogInformation("Starting Kafka consumer for topic: {Topic}", _config.Topic);
            
            await _initializer.EnsureTopicExists(_config.Topic);
            
            _consumer.Subscribe(_config.Topic);

            // Run the consume loop in a separate task
            _consumeTask = Task.Run(async () =>
            {
                try
                {
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

                            await ProcessMessageAsync(consumeResult, stoppingToken);
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

            await Task.WhenAny(_consumeTask, Task.Delay(Timeout.Infinite, stoppingToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ExecuteAsync");
        }
    }

    private async Task ProcessMessageAsync(ConsumeResult<string, string> result, CancellationToken stoppingToken)
    {
        try
        {
            if (result.Message.Key != _expectedEventType)
            {
                // Don't try to store offset separately, just commit directly
                _consumer.Commit(result);
                return;
            }

            var domainEvent = JsonSerializer.Deserialize<TEvent>(
                result.Message.Value,
                JsonConfig.CreateDefault());

            if (domainEvent == null)
            {
                _logger.LogError("Failed to deserialize event of type {EventType}", _expectedEventType);
                _consumer.Commit(result);
                return;
            }

            if (domainEvent.GetType() != typeof(TEvent))
            {
                _logger.LogWarning(
                    "Event type mismatch. Expected {ExpectedType} but got {ActualType}",
                    typeof(TEvent).Name,
                    domainEvent.GetType().Name);
                _consumer.Commit(result);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<ILocationEventHandler<TEvent>>();

            await handler.HandleAsync(domainEvent, stoppingToken);
        
            // Remove the separate StoreOffset call, just commit
            _consumer.Commit(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message");
            // Optionally commit even on error to prevent reprocessing
            // _consumer.Commit(result);
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
            _consumer.Close();
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
