// src/Infrastructure/Messaging/KafkaSettings.cs
namespace Turboapi.Infrastructure.Messaging
{
    public class KafkaSettings
    {
        public string BootstrapServers { get; set; } = "localhost:9092"; // Default for local dev
        public string UserAccountsTopic { get; set; } = "user-accounts-events"; // Default topic name
        public int MessageTimeoutMs { get; set; } = 5000;
        public int RequestTimeoutMs { get; set; } = 5000;
        public int MessageSendMaxRetries { get; set; } = 3;

        // Optional: SSL/SASL settings for production
        // public string? SecurityProtocol { get; set; }
        // public string? SslCaLocation { get; set; }
        // public string? SaslMechanism { get; set; }
        // public string? SaslUsername { get; set; }
        // public string? SaslPassword { get; set; }
    }
}