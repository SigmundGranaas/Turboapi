using Medo;
using Turboauth_activity.domain;
using Turboauth_activity.domain.command;
using Turboauth_activity.domain.events;
using Turboauth_activity.domain.handler;
using Xunit;

namespace Turboauth_activity.test.domain;

public class CreateActivityHandlerTest
{
    [Fact]
    public async Task ExecuteCreateActivityCommand()
    {
        // Arrange
        var owner = Uuid7.NewUuid7();
        var lat = 56.7;
        var lng = 57.7;
        var pos = new Position
        {
            Latitude = lat,
            Longitude = lng,
        };
        
        var name = "Test Activity";
        var icon = "activity-icon";
        var description = "Test Activity description";

        var bus = new TestMessageBus();
        var eventWriter = new TestEventStoreWriter(bus, GetAggregateId);
        
        var handler = new CreateActivityHandler(eventWriter);
        var command = new CreateActivityCommand
        {
            OwnerId = owner,
            Position = pos,
            Name = name,
            Description = description,
            Icon = icon,
        };

        Guid id = await handler.Handle(command);

        // Event created for the new activity and the new position via the eventWriter
        Assert.Contains(bus.Events, (domainEvent) => domainEvent is ActivityCreated activityCreated && activityCreated.activity == id);
        Assert.Contains(bus.Events, (domainEvent) => domainEvent is ActivityCreated activityCreated && activityCreated.name == name);
        
        // Location also created
        Assert.Contains(bus.Events, (domainEvent) => domainEvent is ActivityPositionCreated);
    }
    
    private static Guid GetAggregateId(Event @event) => @event switch
    {
        ActivityCreated e => e.activity,
        ActivityPositionCreated e => e.positionId,
        _ => throw new ArgumentException($"Unknown event type: {@event.GetType()}")
    };
}