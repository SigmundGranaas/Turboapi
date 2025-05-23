
public interface IEventHandler<in T> where T : class
{
    Task HandleAsync(T @event, CancellationToken cancellationToken);
}
