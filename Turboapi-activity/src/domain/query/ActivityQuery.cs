namespace Turboauth_activity.domain.query;

public class ActivityQuery
{
    public record GetActivityByIdQuery(Guid ActivityId, Guid UserId);
}