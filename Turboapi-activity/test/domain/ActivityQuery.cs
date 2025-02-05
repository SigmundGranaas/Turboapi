
using System.Diagnostics;
using Turboauth_activity.domain;
using Turboauth_activity.domain.query;
using Xunit;
using Activity = Turboauth_activity.domain.Activity;

namespace Turboauth_activity.test.domain;

public class ActivityQuery
{
    [Fact]
    public async Task getActivityById()
    {
        var name = "name";
        var description = "email";
        var icon = "icon";
        Guid owner = Guid.NewGuid();
        var pos = new Position
        {
            Latitude = 47.789,
            Longitude = -122.451
        };
        var activity = Activity.Create(owner, pos, name, description, icon);
        var dict = new Dictionary<Guid, Activity>();
        dict.Add(activity.Id, activity);
        var repo = new InMemoryActivityReadModel(dict);
        var handler = new ActivityQueryHandler(repo);
        
        var query = new Turboauth_activity.domain.query.ActivityQuery.GetActivityByIdQuery(activity.Id, owner);
        
        var response = await handler.Handle(query);
        
        Assert.NotNull(response);
    }
    
    [Fact]
    public async Task cannotGetActivityWithWrongOwner()
    {
        var name = "name";
        var description = "email";
        var icon = "icon";
        Guid owner = Guid.NewGuid();
        Guid wrongOwner = Guid.NewGuid();
        var pos = new Position
        {
            Latitude = 47.789,
            Longitude = -122.451
        };
        var activity = Activity.Create(owner, pos, name, description, icon);
        var dict = new Dictionary<Guid, Activity>();
        dict.Add(activity.Id, activity);
        var repo = new InMemoryActivityReadModel(dict);
        var handler = new ActivityQueryHandler(repo);
        
        var query = new Turboauth_activity.domain.query.ActivityQuery.GetActivityByIdQuery(activity.Id, wrongOwner);
        
        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
                async ()  => await handler.Handle(query)
        );
    }

}