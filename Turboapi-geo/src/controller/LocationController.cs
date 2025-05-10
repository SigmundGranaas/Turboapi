using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Turboapi_geo.controller.request;
using Turboapi_geo.controller.response;
using Turboapi_geo.domain.commands;
using Turboapi_geo.domain.exception;
using Turboapi_geo.domain.handler;
using Turboapi_geo.domain.queries;
using Turboapi_geo.domain.query;
using Turboapi_geo.domain.value;

namespace Turboapi_geo.controller;

[ApiController]
[Route("api/geo/[controller]")]
[Authorize]
public class LocationsController : ControllerBase
{
    private readonly CreateLocationHandler _createHandler;
    private readonly UpdateLocationHandler _updateHandler;
    private readonly DeleteLocationHandler _deleteHandler;
    private readonly GetLocationByIdHandler _locationQueryHandler;
    private readonly GetLocationsInExtentHandler _locationsQueryHandler;
    private readonly ILogger<LocationsController> _logger;

    public LocationsController(
        CreateLocationHandler createHandler,
        UpdateLocationHandler updateHandler,
        DeleteLocationHandler deleteHandler,
        GetLocationByIdHandler idQuery,
        GetLocationsInExtentHandler locationsQuery,
        ILogger<LocationsController> logger)
    {
        _createHandler = createHandler;
        _locationQueryHandler = idQuery;
        _locationsQueryHandler = locationsQuery;
        _updateHandler = updateHandler;
        _deleteHandler = deleteHandler;
        _logger = logger;
    }

    private Guid GetAuthenticatedUserId()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId == null)
        {
            throw new UnauthorizedAccessException("User ID not found in token");
        }
        return Guid.Parse(userId);
    }

    [HttpPost]
    [ProducesResponseType(typeof(LocationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LocationResponse>> Create([FromBody] CreateLocationRequest request)
    {
        try
        {
            var userId = GetAuthenticatedUserId();

            var displayInformation = new DisplayInformation(request.Display.Name, request.Display.Description ?? "",
                request.Display.Icon ?? "");
            
            var command = new CreateLocationCommand(
                userId,
                new Coordinates(request.Geometry.Longitude, request.Geometry.Latitude),
                displayInformation
            );

            var locationId = await _createHandler.Handle(command);
            var location = await _locationQueryHandler.Handle(new GetLocationByIdQuery(locationId, userId));
            
            return CreatedAtAction(
                nameof(GetById), 
                new { id = locationId }, 
                LocationResponse.FromDto(location)
            );
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating location");
            return BadRequest(new ErrorResponse("Failed to create location", ex.Message));
        }
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(LocationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<LocationResponse>> Update(
        Guid id,
        [FromBody] UpdateLocationRequest request)
    {
        try
        {
            var userId = GetAuthenticatedUserId();

            Coordinates? newCoordinates = null;
            if (request.Geometry?.Longitude != null && request.Geometry?.Latitude != null)
            {
                newCoordinates = new Coordinates(request.Geometry.Longitude, request.Geometry.Latitude);
            }

            DisplayUpdate? domainDisplayChanges = null;
            if (request.Display != null) // request.Display is controller.request.DisplayChangeset
            {
                // Map from controller's DisplayChangeset to domain's DisplayUpdate
                domainDisplayChanges = new DisplayUpdate(
                    request.Display.Name,
                    request.Display.Description,
                    request.Display.Icon
                );
            }

            // Create the unified domain update parameters object
            var locationUpdateParams = new LocationUpdateParameters(newCoordinates, domainDisplayChanges);

            var command = new UpdateLocationCommand(
                userId,
                id,
                locationUpdateParams // Pass the unified parameters
            );

            await _updateHandler.Handle(command);
            var location = await _locationQueryHandler.Handle(new GetLocationByIdQuery(id, userId));

            return Ok(LocationResponse.FromDto(location));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (LocationNotFoundException)
        {
            return NotFound(new ErrorResponse("Location not found", $"Location with ID {id} was not found"));
        }
        catch (ArgumentException ex) // Catch validation errors from command constructor
        {
            _logger.LogWarning(ex, "Invalid update request for location {LocationId}: {ErrorMessage}", id, ex.Message);
            return BadRequest(new ErrorResponse("Invalid update request", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating location {LocationId}", id);
            return BadRequest(new ErrorResponse("Failed to update location", ex.Message));
        }
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            var userId = GetAuthenticatedUserId();
            var command = new DeleteLocationCommand(userId, id);

            await _deleteHandler.Handle(command);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (LocationNotFoundException)
        {
            return NotFound(new ErrorResponse("Location not found", $"Location with ID {id} was not found"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting location");
            return BadRequest(new ErrorResponse("Failed to delete location", ex.Message));
        }
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(LocationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<LocationResponse>> GetById(Guid id)
    {
        try
        {
            var userId = GetAuthenticatedUserId();
            var location = await _locationQueryHandler.Handle(new GetLocationByIdQuery(id, userId));
            
            if (location == null)
                return NotFound(new ErrorResponse("Location not found", $"Location with ID {id} was not found"));

            return Ok(LocationResponse.FromDto(location));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving location");
            return BadRequest(new ErrorResponse("Failed to retrieve location", ex.Message));
        }
    }

    [HttpGet]
    [ProducesResponseType(typeof(LocationsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<LocationsResponse>> GetInExtent([FromQuery] ExtentQuery query)
    {
        try
        {
            var userId = GetAuthenticatedUserId();
            
            var locations = await _locationsQueryHandler.Handle(new GetLocationsInExtentQuery(
                userId,
                query.Extent.MinLongitude,
                query.Extent.MinLatitude,
                query.Extent.MaxLongitude,
                query.Extent.MaxLatitude
            ));

            return Ok(new LocationsResponse
            {
                Items = locations.Select(LocationResponse.FromDto).ToList(),
                Count = locations.Count()
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving locations in extent");
            return BadRequest(new ErrorResponse("Failed to retrieve locations", ex.Message));
        }
    }
}