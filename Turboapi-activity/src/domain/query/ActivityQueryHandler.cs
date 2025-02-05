using Turboauth_activity.domain.exception;

namespace Turboauth_activity.domain.query;

public class ActivityQueryHandler
{
    private readonly IActivityReadRepository _repository;

    public ActivityQueryHandler(IActivityReadRepository repository)
    {
        _repository = repository;
    }

    public async Task<ActivityQueryDto?> Handle(ActivityQuery.GetActivityByIdQuery query)
    {
        var activity = await _repository.GetById(query.ActivityId);
        if (activity == null)
        {
            throw new ActivityNotFoundException("Activity not found");
        }

        var notAllowed = !activity.CanSeeActivity(query.UserId);
        if (notAllowed)
        {
            throw new UnauthorizedAccessException("You are not allowed to see this activity");
        }
        
        var dto = new ActivityQueryDto()
        {
            Position = activity.Position,
            ActivityId = activity.Id,
            OwnerId = activity.OwnerId,
            Name = activity.Name,
            Description = activity.Description,
            Icon = activity.Icon,
        };
        return dto;
    }
}