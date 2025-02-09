using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Turbo_event.kafka;

public class KafkaConsumer<TEvent> : BackgroundService where TEvent : class
{
    private readonly IConsumer<string, string> _consumer;
    private readonly KafkaMessageProcessor<TEvent> _processor;
    private readonly ITopicInitializer _topicInitializer;
    private readonly string _topic;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stopConsumer;
    private volatile bool _isRunning;
    private Task _consumeTask;

    public KafkaConsumer(
        ITopicInitializer topicInitializer,
        IOptions<KafkaSettings> settings,  
        KafkaMessageProcessor<TEvent> processor,
        ILogger<KafkaConsumer<TEvent>> logger)
    {
        _topicInitializer = topicInitializer;
        _processor = processor;
        _logger = logger;
        _stopConsumer = new CancellationTokenSource();
        ArgumentNullException.ThrowIfNull(settings?.Value);
        _topic = settings.Value.Topic;

        var config = new ConsumerConfig
        {
            BootstrapServers = settings.Value.BootstrapServers,
            GroupId = settings.Value.ConsumerGroupId,
            AutoOffsetReset = settings.Value.AutoOffsetReset,
            EnableAutoCommit = false,
            EnablePartitionEof = true,
            SessionTimeoutMs = 45000,
            MaxPollIntervalMs = 300000,
            HeartbeatIntervalMs = 3000,
            SecurityProtocol = settings.Value.SecurityProtocol,
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _isRunning = true;
            _logger.LogInformation("Starting Kafka consumer for topic: {Topic}", _topic);
            
            await _topicInitializer.EnsureTopicExists(_topic);

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

            await Task.WhenAny(_consumeTask, Task.Delay(Timeout.Infinite, stoppingToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ExecuteAsync");
        }
    }

    private async Task ProcessMessage(ConsumeResult<string, string> result, CancellationToken token)
    {
        await _processor.ProcessMessageAsync(result, token);
        _consumer.StoreOffset(result);
        _consumer.Commit(result);
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