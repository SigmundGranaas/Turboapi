namespace Turboauth_activity.domain.query;

public interface IActivityReadRepository
{
    Task<Activity?> GetById(Guid id);
}