namespace Turboapi.Application.Interfaces
{
    public interface ICommandHandler<in TCommand, TResponse>
    {
        Task<TResponse> Handle(TCommand command, CancellationToken cancellationToken);
    }
}