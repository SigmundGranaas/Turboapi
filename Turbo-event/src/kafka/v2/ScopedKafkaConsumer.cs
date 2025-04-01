
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using Turbo_event.kafka;
using Microsoft.Extensions.DependencyInjection;

namespace Turboapi.Infrastructure.Kafka
{
    public class ScopedKafkaConsumer<TEvent> : BackgroundService where TEvent : Event
    {
        private readonly IConsumer<string, string> _consumer;
        private readonly ITopicInitializer _topicInitializer;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly KafkaConsumerConfig<TEvent> _config;
        private readonly ILogger<ScopedKafkaConsumer<TEvent>> _logger;
        private readonly string _expectedEventType;
        private readonly CancellationTokenSource _stopConsumer;
        private volatile bool _isRunning;
        private Task _consumeTask;

        public ScopedKafkaConsumer(
            IServiceScopeFactory serviceScopeFactory,
            KafkaConsumerConfig<TEvent> config,
            IOptions<KafkaSettings> settings,
            ITopicInitializer topicInitializer,
            IKafkaConsumerFactory consumerFactory,
            ILogger<ScopedKafkaConsumer<TEvent>> logger)
        {
            _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _topicInitializer = topicInitializer ?? throw new ArgumentNullException(nameof(topicInitializer));
            _expectedEventType = typeof(TEvent).Name;
            _stopConsumer = new CancellationTokenSource();

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

            _consumer = consumerFactory.CreateConsumer(consumerConfig);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _isRunning = true;
                _logger.LogInformation("Starting Kafka consumer for topic: {Topic}", _config.Topic);
                
                await _topicInitializer.EnsureTopicExists(_config.Topic);
                
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
                    _logger.LogDebug("Skipping message with key {Key} (expected {ExpectedType})", 
                        result.Message.Key, _expectedEventType);
                    _consumer.Commit(result);
                    return;
                }

                var domainEvent = JsonSerializer.Deserialize<TEvent>(
                    result.Message.Value);

                if (domainEvent == null)
                {
                    _logger.LogError("Failed to deserialize event of type {EventType}", _expectedEventType);
                    _consumer.Commit(result);
                    return;
                }
                
                // Create a new scope for handling the event
                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    // Resolve the handler within the scope
                    var eventHandler = scope.ServiceProvider.GetRequiredService<IEventHandler<TEvent>>();
                    await eventHandler.HandleAsync(domainEvent, stoppingToken);
                }
                
                _consumer.Commit(result);
                
                _logger.LogDebug("Successfully processed and committed event of type {EventType}", _expectedEventType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message of type {EventType}", _expectedEventType);
                _consumer.Commit(result);
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
}