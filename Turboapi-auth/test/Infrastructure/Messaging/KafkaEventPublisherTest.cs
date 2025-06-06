using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using Turboapi.Domain.Events;
using Turboapi.Infrastructure.Messaging;
using Turboapi.Integration.Tests.Fixtures; 
using Xunit;
using Xunit.Abstractions;

namespace Turboapi.Integration.Tests.Messaging
{
    [Collection("KafkaCollection")]
    public class KafkaEventPublisherIntegrationTests : IDisposable
    {
        private readonly KafkaContainerFixture _kafkaFixture;
        private readonly KafkaEventPublisher _publisher;
        private readonly KafkaSettings _kafkaSettings;
        private readonly ILogger<KafkaEventPublisher> _publisherLogger;
        private readonly ITestOutputHelper _output;

        public KafkaEventPublisherIntegrationTests(KafkaContainerFixture kafkaFixture, ITestOutputHelper output)
        {
            _kafkaFixture = kafkaFixture;
            _output = output;

            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddMXLogger(output) // MXLogger is a helper for Xunit output
                    .SetMinimumLevel(LogLevel.Debug);
            });
            _publisherLogger = loggerFactory.CreateLogger<KafkaEventPublisher>();

            _kafkaSettings = new KafkaSettings
            {
                BootstrapServers = _kafkaFixture.BootstrapServers,
                UserAccountsTopic = _kafkaFixture.TestTopic // Use the dynamic test topic from fixture
            };
            var options = Options.Create(_kafkaSettings);
            _publisher = new KafkaEventPublisher(options, _publisherLogger);
        }

        [Fact]
        public async Task PublishAsync_ShouldSendEventToKafkaTopic()
        {
            // Arrange
            var accountId = Guid.NewGuid();
            var email = "test@example.com";
            var roles = new List<string> { "User" };
            var accountCreatedEvent = new AccountCreatedEvent(accountId, email, DateTime.UtcNow, roles);

            var consumerConfig = new ConsumerConfig
            {
                BootstrapServers = _kafkaFixture.BootstrapServers,
                GroupId = $"test-consumer-group-{Guid.NewGuid()}", // Unique group ID for each test run
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false // Allow manual commit or rely on dispose
            };

            using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
            consumer.Subscribe(_kafkaFixture.TestTopic);
            _output.WriteLine($"Consumer subscribed to topic: {_kafkaFixture.TestTopic}");

            // Act
            _output.WriteLine($"Publishing event: {JsonSerializer.Serialize(accountCreatedEvent)}");
            await _publisher.PublishAsync(accountCreatedEvent);
            _output.WriteLine("Event published. Attempting to consume...");

            // Assert
            ConsumeResult<string, string>? consumeResult = null;
            try
            {
                // Increased timeout for CI environments
                consumeResult = consumer.Consume(TimeSpan.FromSeconds(20)); 
            }
            catch (ConsumeException ex)
            {
                 _output.WriteLine($"ConsumeException: {ex.Error.Reason} ({ex.Error.Code})");
                 // Fall through to Assert.NotNull for consumeResult for a clearer failure message
            }


            Assert.NotNull(consumeResult);
            Assert.NotNull(consumeResult.Message);
            _output.WriteLine($"Consumed message: Key='{consumeResult.Message.Key}', Value='{consumeResult.Message.Value}'");


            var deserializedEvent = JsonSerializer.Deserialize<AccountCreatedEvent>(consumeResult.Message.Value);
            Assert.NotNull(deserializedEvent);
            Assert.Equal(accountId.ToString(), consumeResult.Message.Key); // Check if key is AccountId
            Assert.Equal(accountCreatedEvent.AccountId, deserializedEvent.AccountId);
            Assert.Equal(accountCreatedEvent.Email, deserializedEvent.Email);
            Assert.Equal(accountCreatedEvent.CreatedAt.Kind, deserializedEvent.CreatedAt.Kind);
            // For DateTime comparison, allow a small difference due to serialization/deserialization and precision
            Assert.True(Math.Abs((accountCreatedEvent.CreatedAt - deserializedEvent.CreatedAt).TotalMilliseconds) < 1000);
            Assert.Equal(accountCreatedEvent.InitialRoles, deserializedEvent.InitialRoles);

            consumer.Close(); // Close before dispose to commit offset if auto-commit was enabled
        }
        
        public void Dispose()
        {
            _publisher?.Dispose();
        }
    }

    // Helper to pipe ILogger to ITestOutputHelper
    public static class TestOutputHelperExtensions
    {
        public static ILoggingBuilder AddMXLogger(this ILoggingBuilder builder, ITestOutputHelper output)
        {
            builder.AddProvider(new MXLoggerProvider(output));
            return builder;
        }
    }

    public class MXLoggerProvider : ILoggerProvider
    {
        private readonly ITestOutputHelper _output;
        public MXLoggerProvider(ITestOutputHelper output) => _output = output;
        public ILogger CreateLogger(string categoryName) => new MXLogger(_output, categoryName);
        public void Dispose() { }
    }

    public class MXLogger : ILogger
    {
        private readonly ITestOutputHelper _output;
        private readonly string _categoryName;
        public MXLogger(ITestOutputHelper output, string categoryName)
        {
            _output = output;
            _categoryName = categoryName;
        }

        public IDisposable BeginScope<TState>(TState state) => NoopDisposable.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            try
            {
                _output.WriteLine($"{_categoryName} [{logLevel}] {formatter(state, exception)}");
                if (exception != null)
                    _output.WriteLine(exception.ToString());
            }
            catch (InvalidOperationException) // Can happen if test output is already disposed
            { }
        }
        private class NoopDisposable : IDisposable { public static NoopDisposable Instance { get; } = new(); public void Dispose() { } }
    }
}