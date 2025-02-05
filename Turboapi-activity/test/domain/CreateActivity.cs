using Medo;
using Turboauth_activity.domain;
using Turboauth_activity.domain.events;
using Xunit;

namespace Turboauth_activity.test.domain;

public class CreateActivity
{
    
    [Fact]
    public async Task CreateActivityFromBasicParameters()
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
        
        // Act
        var activity = Activity.Create(owner, pos, name, description, icon);
       
        // Assert
        Assert.Equal(activity.Name, name);
        Assert.Equal(activity.Icon, icon);

        // Event created for the new activity and the new position
        Assert.Contains(activity.Events, (domainEvent) => domainEvent is ActivityCreated activityCreated && activityCreated.activity == activity.Id);
        Assert.Contains(activity.Events, (domainEvent) => domainEvent is ActivityPositionCreated position && position.positionId == activity.Position && position.activityId == activity.Id);
    }
    
        
    [Fact]
    public async Task UpdateActivity()
    {
        // Arrange
        var id = Uuid7.NewUuid7();
        var owner = Uuid7.NewUuid7();
        var lat = 56.7;
        var lng = 57.7;
        var pos = Uuid7.NewUuid7();
        
        var name = "Test Activity";
        var icon = "activity-icon";
        var description = "Test Activity description";
        
        // Act
        var activity = Activity.From(id, owner, pos, name, description, icon);
       
        // Assert
        Assert.Equal(activity.Name, name);
        Assert.Equal(activity.Icon, icon);

        // No events are created when using the From method
        Assert.Empty(activity.Events);
        
        var updated = activity.Update(owner, "New Name", "New Description", "dive-icon");
        Assert.Equal("New Name", updated.Name);
        Assert.Contains(updated.Events, (domainEvent) => domainEvent is ActivityUpdated activityUpdated && activityUpdated.name == updated.Name);
        Assert.Equal("dive-icon", updated.Icon);
        Assert.Contains(updated.Events, (domainEvent) => domainEvent is ActivityUpdated activityUpdated && activityUpdated.icon == updated.Icon);
        Assert.Equal("New Description", updated.Description);
        Assert.Contains(updated.Events, (domainEvent) => domainEvent is ActivityUpdated activityUpdated && activityUpdated.description == updated.Description);
    }
}