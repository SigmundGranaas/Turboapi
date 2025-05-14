using GeoSpatial.Domain.Events;
using Turboapi_geo.data;
using Turboapi_geo.domain.commands;
using Turboapi_geo.domain.exception;
using Turboapi_geo.domain.query;

namespace Turboapi_geo.domain.handler;

    public class DeleteLocationHandler
    {
        private readonly IEventWriter _eventWriter;
        private readonly ILocationReadRepository _locationReadRepository;
        private readonly IDirectReadModelProjector _readModelHandler;

        public DeleteLocationHandler(
            IEventWriter eventWriter, ILocationReadRepository locationReadRepository, IDirectReadModelProjector readModelHandler)
        {
            _eventWriter = eventWriter;
            _locationReadRepository = locationReadRepository;
            _readModelHandler = readModelHandler;
        }

        public async Task Handle(DeleteLocationCommand command)
        {
            var location = await _locationReadRepository.GetById(command.LocationId);
            if (location == null)
                throw new LocationNotFoundException($"Location with ID {command.LocationId} not found");
            
            location.Delete(command.UserId);
            
            await _readModelHandler.ProjectEventsAsync(location.Events);
            await _eventWriter.AppendEvents(location.Events);
        }
}