using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Turboapi_geo.domain.query.model;

public class EfLocationWriteRepository
{
       public class EfLocationWriteModelRepository : ILocationWriteRepository
    {
        private readonly LocationReadContext _context;

        public EfLocationWriteModelRepository(LocationReadContext context)
        {
            _context = context;
        }

        public async Task<LocationReadEntity?> GetById(Guid id)
        {
            return await _context.Locations.FindAsync(id);
        }
        
        public async Task Add(LocationReadEntity entity)
        {
            _context.Locations.Add(entity);
            await _context.SaveChangesAsync();
        }

        public async Task Update(LocationReadEntity entity)
        {
            _context.Entry(entity).State = EntityState.Modified;
            await _context.SaveChangesAsync();
        }
        
        public async Task Delete(LocationReadEntity entity)
        {
            _context.Locations.Remove(entity);
            await _context.SaveChangesAsync();
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
            else
            {
                return Location.From(location.Id, location.OwnerId, location.Geometry);

            }
        }
        
        public async Task<IEnumerable<Location>> GetLocationsInExtent(
            string ownerId,
            double minLongitude,
            double minLatitude,
            double maxLongitude,
            double maxLatitude
           )
        {
            var geometryFactory = new GeometryFactory(new PrecisionModel(), 3857);
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
            
            return results.Select(l => Location.From(l.Id, l.OwnerId, l.Geometry));
        }
    }
}