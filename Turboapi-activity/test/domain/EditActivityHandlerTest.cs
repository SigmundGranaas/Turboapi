using Medo;
using Turboauth_activity.domain;
using Turboauth_activity.domain.command;
using Turboauth_activity.domain.events;
using Turboauth_activity.domain.handler;
using Turboauth_activity.domain.query;
using Xunit;

namespace Turboauth_activity.test.domain;

public class EditActivityHandlerTest
{
    [Fact]
    public async Task ExecuteEditActivityCommand()
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
        var handler = new EditActivityHandler(eventWriter, repo);
        var command = new EditActivityCommand
        {
            ActivityID = created.Id,
            UserID = owner,
            Name = name,
            Description = description,
            Icon = icon,
        };

        ActivityQueryDto dto = await handler.Handle(command);

        Assert.Contains(bus.Events, (domainEvent) => domainEvent is ActivityUpdated activityUpdated && activityUpdated.ActivityId == dto.ActivityId);
    }
    
    private static Guid GetAggregateId(Event @event) => @event switch
    {
        ActivityUpdated e => e.ActivityId,
        _ => throw new ArgumentException($"Unknown event type: {@event.GetType()}")
    };
}