using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Testcontainers.Kafka;
using Xunit;

namespace Turboapi.Integration.Tests.Fixtures
{
    public class KafkaContainerFixture : IAsyncLifetime
    {
        public KafkaContainer KafkaContainer { get; }
        private readonly ILogger<KafkaContainerFixture> _logger;
        public string BootstrapServers => KafkaContainer.GetBootstrapAddress();
        public string TestTopic { get; } = $"test-topic-{Guid.NewGuid()}";

        public KafkaContainerFixture()
        {
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddFilter("Default", LogLevel.Information)
                    .AddFilter("Microsoft", LogLevel.Warning)
                    .AddFilter("Testcontainers", LogLevel.Information) 
                    .AddConsole();
            });
            _logger = loggerFactory.CreateLogger<KafkaContainerFixture>();

            KafkaContainer = new KafkaBuilder()
                .WithImage("confluentinc/cp-kafka:latest")
                .Build();
        }

        public async Task InitializeAsync()
        {
            _logger.LogInformation("Starting Kafka container...");
            await KafkaContainer.StartAsync();
            _logger.LogInformation("Kafka container started. Bootstrap Servers: {BootstrapServers}", BootstrapServers);
            await CreateTestTopicAsync();
        }

        private async Task CreateTestTopicAsync()
        {
            _logger.LogInformation("Creating test topic: {TestTopic}", TestTopic);
            using var adminClient = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = BootstrapServers }).Build();
            try
            {
                await adminClient.CreateTopicsAsync(new TopicSpecification[]
                {
                    new() { Name = TestTopic, ReplicationFactor = 1, NumPartitions = 1 }
                });
                _logger.LogInformation("Test topic {TestTopic} created successfully.", TestTopic);
            }
            catch (CreateTopicsException e) when (e.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
            {
                _logger.LogWarning("Test topic {TestTopic} already exists.", TestTopic);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create test topic {TestTopic}", TestTopic);
                throw;
            }
        }

        public async Task DisposeAsync()
        {
            _logger.LogInformation("Stopping Kafka container...");
            if (KafkaContainer != null) // Check if container was initialized
            {
                 await KafkaContainer.StopAsync();
                 await KafkaContainer.DisposeAsync();
            }
            _logger.LogInformation("Kafka container stopped and disposed.");
        }
    }

    [CollectionDefinition("KafkaCollection")]
    public class KafkaCollection : ICollectionFixture<KafkaContainerFixture>
    {
        // This class has no code, and is never created. Its purpose is simply
        // to be the place to apply [CollectionDefinition] and all the
        // ICollectionFixture<> interfaces.
    }
}