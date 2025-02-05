using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Turboauth_activity.domain;
using Turboauth_activity.domain.command;
using Turboauth_activity.domain.handler;
using Turboauth_activity.domain.query;

namespace Turboauth_activity.controller;

[ApiController]
[Route("api/[controller]")]
public class ActivityController: ControllerBase
{
    private readonly CreateActivityHandler _handler;
    private readonly ActivityQueryHandler _queryHandler;
    private readonly EditActivityHandler _editHandler;
    private readonly DeleteActivityHandler _deleteHandler;

    public ActivityController(CreateActivityHandler handler, ActivityQueryHandler queryHandler, EditActivityHandler editHandler, DeleteActivityHandler deleteHandler)
    {
        _queryHandler = queryHandler;
        _handler = handler;
        _editHandler = editHandler;
        _deleteHandler = deleteHandler;
    }
    
    
    [HttpPost]
    [ProducesResponseType(typeof(CreateActivityResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateActivityResponse>> Create(
        [FromBody] CreateActivityRequest request)
    {
        var userId = HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId == null)
        {
            return Forbid();
        }
        
     var command = new CreateActivityCommand
     {
         OwnerId = Guid.Parse(userId),
         Position = request.Position,
         Name = request.Name,
         Description = request.Description,
         Icon = request.Icon,
     };
     var activityId = await _handler.Handle(command);
     
     return CreatedAtAction(  
         nameof(Create), 
         new { id = activityId },
         new CreateActivityResponse(activityId));
    }
    
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ActivityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ActivityResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ActivityResponse>> Get(
        [FromRoute] Guid id)
    {
        var userId = HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId == null)
        {
            return Forbid();
        }

        var query = new ActivityQuery.GetActivityByIdQuery(id, Guid.Parse(userId));
        var activity = await _queryHandler.Handle(query);
        if (activity == null)
        {
            return NotFound();
        }

        var response = new ActivityResponse(activity.ActivityId, activity.OwnerId, activity.Position, activity.Name,
            activity.Description, activity.Icon);
        return Ok(response);
    }
    
    [HttpGet]
    [ProducesResponseType(typeof(ActivityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ActivityResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ActivityResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ActivityResponse>> EditActivityById(
        [FromBody] EditActivityRequest request, [FromRoute] Guid activityId)
    {
        var userId = HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId == null)
        {
            return Forbid();
        }

        var query = new EditActivityCommand
        {
            UserID = new Guid(userId),
            ActivityID = activityId,
            Name = request.Name,
            Description = request.Description,
            Icon = request.Icon,
        };
        var activity = await _editHandler.Handle(query);
        
        if (activity == null)
        {
            return NotFound();
        }

        var response = new ActivityResponse(activity.ActivityId, activity.OwnerId, activity.Position, activity.Name,
            activity.Description, activity.Icon);
        return Ok(response);
    }

    [HttpGet]
    [ProducesResponseType(typeof(DeletedActivityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(DeletedActivityResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DeletedActivityResponse>> DeleteActivityById(
        [FromQuery] Guid id)
    {
        var userId = HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId == null)
        {
            return Forbid();
        }

        var guid = await _deleteHandler.Handle(new DeleteActivityCommand { ActivityID = id , UserID = new Guid(userId) });

        var response = new DeletedActivityResponse(guid);
        return Ok(response);
    }
    
    public record CreateActivityRequest(
        Position Position,
        string Name,
        string Description,
        string Icon
    );
    
    public record EditActivityRequest(
        string Name,
        string Description,
        string Icon
    );
    
    public record ActivityResponse(
        Guid Id,
        Guid OwnerId,
        Guid Position,
        string Name,
        string Description,
        string Icon
    );
    
    public record CreateActivityResponse(
        Guid ActivityId
    );
    
    public record DeletedActivityResponse(
        Guid ActivityId
    );
}