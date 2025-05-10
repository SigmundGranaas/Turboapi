using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Turboapi_geo.data.model;
using Turboapi_geo.domain.query;
using Turboapi_geo.domain.query.model;
using Turboapi_geo.domain.value;
using Coordinates = Turboapi_geo.domain.value.Coordinates;
using Location = Turboapi_geo.domain.model.Location;

namespace Turboapi_geo.data;

public class EfLocationWriteRepository : ILocationWriteRepository
{
    private readonly LocationReadContext _context;
    private readonly ILogger<EfLocationWriteRepository> _logger;

    public EfLocationWriteRepository(
        LocationReadContext context,
        ILogger<EfLocationWriteRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<LocationEntity?> GetById(Guid id)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await _context.Locations.FindAsync(id);
        stopwatch.Stop();
        
        _logger.LogDebug("GetById for {LocationId} completed in {ElapsedMs}ms", 
            id, stopwatch.ElapsedMilliseconds);
        
        return result;
    }
    
    public async Task Add(LocationEntity entity)
    {
        var stopwatch = Stopwatch.StartNew();
        _context.Locations.Add(entity);
        await _context.SaveChangesAsync();
        stopwatch.Stop();
        
        _logger.LogInformation("Added location {LocationId} in {ElapsedMs}ms", 
            entity.Id, stopwatch.ElapsedMilliseconds);
    }

    public async Task Update(LocationEntity entity)
    {
        var stopwatch = Stopwatch.StartNew();
        _context.Entry(entity).State = EntityState.Modified;
        await _context.SaveChangesAsync();
        stopwatch.Stop();
        
        _logger.LogInformation("Updated location {LocationId} (full update) in {ElapsedMs}ms", 
            entity.Id, stopwatch.ElapsedMilliseconds);
    }
    
    public async Task Delete(LocationEntity entity)
    {
        var stopwatch = Stopwatch.StartNew();
        _context.Locations.Remove(entity);
        await _context.SaveChangesAsync();
        stopwatch.Stop();
        
        _logger.LogInformation("Deleted location {LocationId} in {ElapsedMs}ms", 
            entity.Id, stopwatch.ElapsedMilliseconds);
    }
    
    public async Task UpdatePartial(Guid id, Coordinates? geometry, DisplayUpdate? displayInformation)
    {
        var stopwatch = Stopwatch.StartNew();

        if (geometry != null)
        {
            var factory = new GeometryFactory();
            await _context.Locations
                .Where(l => l.Id == id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(l => l.Geometry, geometry.ToPoint(factory)));
        }

        if (displayInformation != null)
        {
             await _context.Locations
                .Where(l => l.Id == id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(l => l.Name, displayInformation.Name)
                    .SetProperty(l => l.Description, displayInformation.Description)
                    .SetProperty(l => l.Icon, displayInformation.Icon));
        }
        stopwatch.Stop();
        
        _logger.LogInformation("Updated position for location {LocationId} in {ElapsedMs}ms", 
            id, stopwatch.ElapsedMilliseconds);
    }


    public class EfLocationReadRepository : ILocationReadRepository
    {
        private readonly LocationReadContext _context;

        public EfLocationReadRepository(LocationReadContext context)
        {
            _context = context;
        }


        public async Task<Location?> GetById(Guid id)
        {
            var location = await _context.Locations.FindAsync(id);

            if (location == null)
            {
                return null;
            }

            return Location.Reconstitute(location.Id, location.OwnerId, Coordinates.FromPoint(location.Geometry),
                new DisplayInformation(location.Name, location.Description, location.Icon));
        }

        public async Task<IEnumerable<Location>> GetLocationsInExtent(
            Guid ownerId,
            double minLongitude,
            double minLatitude,
            double maxLongitude,
            double maxLatitude
        )
        {
            var geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
            var extent = geometryFactory.CreatePolygon(new Coordinate[]
            {
                new(minLongitude, minLatitude),
                new(maxLongitude, minLatitude),
                new(maxLongitude, maxLatitude),
                new(minLongitude, maxLatitude),
                new(minLongitude, minLatitude)
            });

            var query = _context.Locations
                .AsNoTracking()
                .Where(l => l.OwnerId == ownerId)
                .Where(l => extent.Contains(l.Geometry));

            var results = await query.ToListAsync();

            return results.Select(l => Location.Reconstitute(l.Id, l.OwnerId, Coordinates.FromPoint(l.Geometry),
                new DisplayInformation(l.Name, l.Description, l.Icon)));
        }
    }
}