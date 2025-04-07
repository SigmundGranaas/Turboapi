using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Turboapi_geo.domain.value;

namespace Turboapi_geo.domain.query.model;


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

    public async Task<LocationReadEntity?> GetById(Guid id)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await _context.Locations.FindAsync(id);
        stopwatch.Stop();
        
        _logger.LogDebug("GetById for {LocationId} completed in {ElapsedMs}ms", 
            id, stopwatch.ElapsedMilliseconds);
        
        return result;
    }
    
    public async Task Add(LocationReadEntity entity)
    {
        var stopwatch = Stopwatch.StartNew();
        _context.Locations.Add(entity);
        await _context.SaveChangesAsync();
        stopwatch.Stop();
        
        _logger.LogInformation("Added location {LocationId} in {ElapsedMs}ms", 
            entity.Id, stopwatch.ElapsedMilliseconds);
    }

    public async Task Update(LocationReadEntity entity)
    {
        var stopwatch = Stopwatch.StartNew();
        _context.Entry(entity).State = EntityState.Modified;
        await _context.SaveChangesAsync();
        stopwatch.Stop();
        
        _logger.LogInformation("Updated location {LocationId} (full update) in {ElapsedMs}ms", 
            entity.Id, stopwatch.ElapsedMilliseconds);
    }
    
    public async Task Delete(LocationReadEntity entity)
    {
        var stopwatch = Stopwatch.StartNew();
        _context.Locations.Remove(entity);
        await _context.SaveChangesAsync();
        stopwatch.Stop();
        
        _logger.LogInformation("Deleted location {LocationId} in {ElapsedMs}ms", 
            entity.Id, stopwatch.ElapsedMilliseconds);
    }
    
    // New partial update methods
    public async Task UpdatePosition(Guid id, Point geometry)
    {
        var stopwatch = Stopwatch.StartNew();
        
        // Execute a direct SQL update to avoid the race condition
        // This only updates the geometry field without touching other fields
        var entity = await _context.Locations
            .Where(l => l.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(l => l.Geometry, geometry));
                
        stopwatch.Stop();
        
        if (entity == 0)
        {
            _logger.LogWarning("Failed to update position for location {LocationId} - entity not found", id);
        }
        else
        {
            _logger.LogInformation("Updated position for location {LocationId} in {ElapsedMs}ms", 
                id, stopwatch.ElapsedMilliseconds);
        }
    }
    
    public async Task UpdateDisplayInformation(Guid id, string name, string? description, string? icon)
    {
        var stopwatch = Stopwatch.StartNew();
        
        // Execute a direct update for display information without modifying the position
        var entity = await _context.Locations
            .Where(l => l.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(l => l.Name, name)
                .SetProperty(l => l.Description, description)
                .SetProperty(l => l.Icon, icon));
                
        stopwatch.Stop();
        
        if (entity == 0)
        {
            _logger.LogWarning("Failed to update display information for location {LocationId} - entity not found", id);
        }
        else
        {
            _logger.LogInformation("Updated display information for location {LocationId} in {ElapsedMs}ms", 
                id, stopwatch.ElapsedMilliseconds);
        }
    }
}

public class EfLocationReadModelRepository : ILocationReadModelRepository
{
    private readonly LocationReadContext _context;

    public EfLocationReadModelRepository(LocationReadContext context)
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

        return Location.From(location.Id, location.OwnerId, location.Geometry, DisplayInformation.of(location.Name, location.Description, location.Icon));
    }
        
    public async Task<IEnumerable<Location>> GetLocationsInExtent(
        string ownerId,
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
        
        return results.Select(l => Location.From(l.Id, l.OwnerId, l.Geometry, new DisplayInformation()
        {
            Name = l.Name,
            Description = l.Description,
            Icon = l.Icon
        }));
    }
}