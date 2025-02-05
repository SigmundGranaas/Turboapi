using Medo;
using Turboauth_activity.domain;
using Turboauth_activity.domain.command;
using Turboauth_activity.domain.events;
using Turboauth_activity.domain.handler;
using Turboauth_activity.domain.query;
using Xunit;

namespace Turboauth_activity.test.domain;

public class DeleteActivityHandlerTest
{
    [Fact]
    public async Task ExecuteDeleteActivityCommand()
    {
        var bus = new TestMessageBus();
        var eventWriter = new TestEventStoreWriter(bus, GetAggregateId);

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
        var created = Activity.Create(owner, pos, name, icon, description);
        
        var dict = new Dictionary<Guid, Activity>();
        dict.Add(created.Id, created);
        
        var repo = new InMemoryActivityReadModel(dict);
        var handler = new DeleteActivityHandler(eventWriter, repo);
            var command = new DeleteActivityCommand
        {
            ActivityID = created.Id,
            UserID = owner
        };

        Guid id = await handler.Handle(command);

        Assert.Contains(bus.Events, (domainEvent) => domainEvent is ActivityDeleted activityDeleted && activityDeleted.activityId == id);
    }
    
    private static Guid GetAggregateId(Event @event) => @event switch
    {
        ActivityDeleted e => e.activityId,
        _ => throw new ArgumentException($"Unknown event type: {@event.GetType()}")
    };
}