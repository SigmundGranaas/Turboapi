using Turboauth_activity.domain.query;

namespace Turboauth_activity.data;

public interface IActivityWriteRepository
{
    Task<ActivityQueryDto?> GetById(Guid id);
    Task Add(ActivityQueryDto entity);
    Task Update(ActivityQueryDto entity);
    Task Delete(ActivityQueryDto entity);
}