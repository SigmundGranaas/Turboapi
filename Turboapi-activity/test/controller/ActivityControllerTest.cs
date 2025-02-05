using System.Security.Claims;
using Medo;
using Microsoft.AspNetCore.Mvc;
using Turboauth_activity.controller;
using Turboauth_activity.domain;
using Turboauth_activity.domain.handler;
using Turboauth_activity.domain.query;
using Xunit;

namespace Turboauth_activity.test.controller;

public class ActivityControllerTest
{
    [Fact]
    public async Task CreateActivity()
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
        var writer = new TestEventStoreWriter(bus);
        var handler = new CreateActivityHandler(writer);
        
        var controller = new ActivityController(handler, null, null, null);
        SetupControllerContext( owner, controller);
        
        var input = new ActivityController.CreateActivityRequest(pos, name, icon, description);
        
        var response = await controller.Create(input);
        
        var result = Assert.IsType<CreatedAtActionResult>(response.Result);
        var activityId = Assert.IsType<ActivityController.CreateActivityResponse>(result.Value);
    }

    [Fact]
    public async Task GetExistingActivity()
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
        var controller = new ActivityController(null, handler, null, null);
        SetupControllerContext(owner, controller);
        
        // Act
        var res = await controller.Get(activity.Id);
        var result = Assert.IsType<OkObjectResult>(res.Result);
        Assert.IsType<ActivityController.ActivityResponse>(result.Value);
    }
    
    [Fact]
    public async Task EditActivity()
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
        
        var bus = new TestMessageBus();
        var writer = new TestEventStoreWriter(bus);
        
        var handler = new EditActivityHandler(writer, repo);
        var controller = new ActivityController(null, null, handler, null);
        SetupControllerContext(owner, controller);
        
        var newName = "Updated name";
        var newDescription = "Updated description";
        var newIcon = "Updated icon";

        var request = new ActivityController.EditActivityRequest(newName, newDescription, newIcon);
        
        // Act
        var res = await controller.EditActivityById(request , activity.Id);
        var result = Assert.IsType<OkObjectResult>(res.Result);
        var response = Assert.IsType<ActivityController.ActivityResponse>(result.Value);
        
        Assert.Equal(activity.Id, response.Id);
        Assert.Equal(newName, response.Name);
        Assert.Equal(newDescription, response.Description);
        Assert.Equal(newIcon, response.Icon);
    }
    
    [Fact]
    public async Task DeleteActivity()
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
        
        var bus = new TestMessageBus();
        var writer = new TestEventStoreWriter(bus);
        
        var handler = new DeleteActivityHandler(writer, repo);
        var controller = new ActivityController(null, null, null, handler);
        SetupControllerContext(owner, controller);
        
        // Act
        var res = await controller.DeleteActivityById(activity.Id);
        var result = Assert.IsType<OkObjectResult>(res.Result);
        var response = Assert.IsType<ActivityController.DeletedActivityResponse>(result.Value);

        Assert.Equal(activity.Id, response.ActivityId);
    }
    
    [Fact]
    public async Task CannotGetWithoutOwner()
    {

        var dict = new Dictionary<Guid, Activity>();
   
        var repo = new InMemoryActivityReadModel(dict);
        var handler = new ActivityQueryHandler(repo);
        var controller = new ActivityController(null, handler, null, null);
        
        // Act
        var res = await controller.Get(Guid.Empty);
        var result = Assert.IsType<ForbidResult>(res.Result);
    }
    
    private void SetupControllerContext(Guid userId, ActivityController controller)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var claimsPrincipal = new ClaimsPrincipal(identity);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = claimsPrincipal
            }
        };
    }
}