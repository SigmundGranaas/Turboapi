using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Turboapi_geo.domain.exception;
using Turboapi_geo.domain.handler;
using Turboapi_geo.domain.queries;
using Turboapi_geo.domain.query;
using Turboapi_geo.domain.value;

namespace Turboapi_geo.controller;

    [ApiController]
    [Route("api/geo/[controller]")]
    public class LocationsController : ControllerBase
    {
        private readonly CreateLocationHandler _createHandler;
        private readonly UpdateLocationPositionHandler _updateHandler;
        private readonly DeleteLocationHandler _deleteHandler;
        private readonly GetLocationByIdHandler _locationQueryHandler;
        private readonly GetLocationsInExtentHandler _locationsQueryHandler;


        public LocationsController(
            CreateLocationHandler createHandler,
            UpdateLocationPositionHandler updateHandler,
            DeleteLocationHandler deleteHandler,
            GetLocationByIdHandler idQuery,
            GetLocationsInExtentHandler locationsQuery
            )
        {
            _createHandler = createHandler;
            _locationQueryHandler = idQuery;
            _locationsQueryHandler = locationsQuery;
            _updateHandler = updateHandler;
            _deleteHandler = deleteHandler;
        }

        [HttpPost]
        [Authorize]
        [ProducesResponseType(typeof(CreateLocationResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<CreateLocationResponse>> Create([FromBody] CreateLocationRequest request)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId == null)
            {
                return Forbid();
            }

            var displayInformation = new DisplayInformation()
            {
                Name = request.display.Name,
                Description = request.display.Description ?? "",
                Icon = request.display.Icon ?? ""
            };
            
            var command = new Commands.CreateLocationCommand(
                Guid.Parse(userId),
                request.location.Longitude,
                request.location.Latitude,
                displayInformation
            );

            var locationId = await _createHandler.Handle(command);

            return CreatedAtAction(
                nameof(GetById), 
                new { id = locationId }, 
                new CreateLocationResponse(locationId)
            );
        }

        [Authorize]
        [HttpPut("{id}/position")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdatePosition(
            Guid id, 
            [FromBody] UpdateLocationRequest request)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId == null)
            {
                return Forbid();
            }
            
            var command = new Commands.UpdateLocationPositionCommand(
                Guid.Parse(userId),
                id,
                request.Location,
                request.Name,
                request.Description,
                request.Icon
            );

            try
            {
                await _updateHandler.Handle(command);
                return NoContent();
            }
            catch (LocationNotFoundException)
            {
                return NotFound();
            }
        }

        [HttpDelete("{id}")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(Guid id)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId == null)
            {
                return Forbid();
            }
            
            var command = new Commands.DeleteLocationCommand(id, Guid.Parse(userId));

            try
            {
                await _deleteHandler.Handle(command);
                return NoContent();
            }
            catch (LocationNotFoundException)
            {
                return NotFound();
            }
        }

        [HttpGet("{id}")]
        [Authorize]
        [ProducesResponseType(typeof(LocationResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<LocationResponse>> GetById(Guid id)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId == null)
            {
                return Forbid();
            }
            
            var location = await _locationQueryHandler.Handle(new GetLocationByIdQuery(id, Guid.Parse(userId)));
            if (location == null)
                return NotFound();

            return Ok(LocationResponse.FromDto(location));
        }

        [Authorize]
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<LocationResponse>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<LocationResponse>>> GetInExtent(
            [FromQuery] double minLon,
            [FromQuery] double minLat,
            [FromQuery] double maxLon,
            [FromQuery] double maxLat)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId == null)
            {
                return Forbid();
            }
            
            var locations = await _locationsQueryHandler.Handle(new GetLocationsInExtentQuery(
                Guid.Parse(userId),
                minLon,
                minLat,
                maxLon,
                maxLat
            ));

            return Ok(locations.Select(LocationResponse.FromDto));
        }
    }

    public record CreateLocationRequest(
        LocationData location,
        DisplayInformationData display
    );

    public record LocationData(
        double Longitude,
        double Latitude);

    public record DisplayInformationData(string Name, string? Description, string? Icon);

    public record UpdateLocationRequest(
        LocationData? Location,
        string? Name = null,
        string? Description = null,
        string? Icon = null
    );

    public record CreateLocationResponse(
        Guid Id
    );

    public record LocationResponse(
        Guid Id,
        double Longitude,
        double Latitude,
        DisplayInformationData DisplayData
        )
    {
        public static LocationResponse FromDto(LocationDto dto) => new(
            dto.id,
            dto.geometry.X,
            dto.geometry.Y,
            new DisplayInformationData(dto.displayInformation.Name, dto.displayInformation.Description, dto.displayInformation.Icon)
        );
    }