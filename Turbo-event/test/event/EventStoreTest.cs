using FluentAssertions;
using Xunit;

public class TestEventStoreWriterTests
{
    private readonly TestMessageBus _messageBus;
    private readonly TestEventStoreWriter _eventStore;
    private readonly Guid _orderId;

    public TestEventStoreWriterTests()
    {
        _messageBus = new TestMessageBus();
        _eventStore = new TestEventStoreWriter(_messageBus, GetAggregateId);
        _orderId = Guid.NewGuid();
    }

    private static Guid GetAggregateId(Event @event) => @event switch
    {
        OrderCreated e => e.OrderId,
        OrderItemAdded e => e.OrderId,
        OrderCancelled e => e.OrderId,
        _ => throw new ArgumentException($"Unknown event type: {@event.GetType()}")
    };

    [Fact]
    public async Task AppendEvents_ShouldPublishEventsToMessageBus()
    {
        // Arrange
        var events = new Event[]
        {
            new OrderCreated { OrderId = _orderId, CustomerName = "Test Customer", TotalAmount = 100m },
            new OrderItemAdded { OrderId = _orderId, ProductName = "Test Product", Quantity = 1, UnitPrice = 100m }
        };

        // Act
        await _eventStore.AppendEvents(events);

        // Assert
        _messageBus.Events.Should().HaveCount(2);
        _messageBus.GetEventsOfType<OrderCreated>().Should().ContainSingle();
        _messageBus.GetEventsOfType<OrderItemAdded>().Should().ContainSingle();
    }

    [Fact]
    public async Task AppendEvents_ShouldIncrementVersionForSameAggregate()
    {
        // Arrange
        var events = new Event[]
        {
            new OrderCreated { OrderId = _orderId, CustomerName = "Test Customer", TotalAmount = 100m },
            new OrderItemAdded { OrderId = _orderId, ProductName = "Product 1", Quantity = 1, UnitPrice = 50m },
            new OrderItemAdded { OrderId = _orderId, ProductName = "Product 2", Quantity = 1, UnitPrice = 50m }
        };

        // Act
        await _eventStore.AppendEvents(events);

        // Assert
        _eventStore.Versions[_orderId].Should().Be(3);
    }

    [Fact]
    public async Task AppendEvents_ShouldTrackVersionsSeparatelyForDifferentAggregates()
    {
        // Arrange
        var orderId1 = Guid.NewGuid();
        var orderId2 = Guid.NewGuid();

        var events = new Event[]
        {
            new OrderCreated { OrderId = orderId1, CustomerName = "Customer 1", TotalAmount = 100m },
            new OrderCreated { OrderId = orderId2, CustomerName = "Customer 2", TotalAmount = 200m },
            new OrderItemAdded { OrderId = orderId1, ProductName = "Product 1", Quantity = 1, UnitPrice = 100m }
        };

        // Act
        await _eventStore.AppendEvents(events);

        // Assert
        _eventStore.Versions[orderId1].Should().Be(2);
        _eventStore.Versions[orderId2].Should().Be(1);
    }
}

public record OrderCreated : Event
{
    public Guid OrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
}

public record OrderItemAdded : Event
{
    public Guid OrderId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public record OrderCancelled : Event
{
    public Guid OrderId { get; set; }
    public string Reason { get; set; } = string.Empty;
}
