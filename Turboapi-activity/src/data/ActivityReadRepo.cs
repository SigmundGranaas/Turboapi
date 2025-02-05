using Microsoft.EntityFrameworkCore;
using Turboauth_activity.domain;
using Turboauth_activity.domain.query;

namespace Turboauth_activity.data;

public class ActivityReadRepository : IActivityReadRepository
{
    private readonly ActivityContext _context;

    public ActivityReadRepository(ActivityContext context)
    {
        _context = context;
    }

    public async Task<Activity?> GetById(Guid id)
    {
        var dto = await _context.Activities
            .FirstOrDefaultAsync(a => a.ActivityId == id);

        return dto == null ? null : Activity.From(dto.ActivityId, dto.OwnerId, dto.Position, dto.Name, dto.Description, dto.Icon);
    }
}