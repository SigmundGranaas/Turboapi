using Turbo.Outbox;
using Turboapi.Application.Interfaces;
using Turboapi.Application.Results;

namespace Turboapi.Application.Behaviors
{
    /// <summary>
    /// Wraps a command handler: on a successful Result, commits the
    /// scope's <see cref="IUnitOfWork{TScope}"/>. Auth's handlers stage
    /// changes through repositories rather than passing a work delegate,
    /// so this decorator hands an empty delegate to SaveChangesAsync —
    /// the UnitOfWork still does its job of wrapping the SaveChanges in
    /// the execution strategy and draining aggregate events into the
    /// outbox before committing.
    /// </summary>
    public class UnitOfWorkCommandHandlerDecorator<TCommand, TResponse> : ICommandHandler<TCommand, TResponse>
    {
        private readonly ICommandHandler<TCommand, TResponse> _decorated;
        private readonly IUnitOfWork<IAuthScope> _unitOfWork;

        public UnitOfWorkCommandHandlerDecorator(
            ICommandHandler<TCommand, TResponse> decorated,
            IUnitOfWork<IAuthScope> unitOfWork)
        {
            _decorated = decorated;
            _unitOfWork = unitOfWork;
        }

        public async Task<TResponse> Handle(TCommand command, CancellationToken cancellationToken)
        {
            var response = await _decorated.Handle(command, cancellationToken);

            if (response is Results.IResult { IsSuccess: true })
            {
                await _unitOfWork.SaveChangesAsync(_ => Task.CompletedTask, cancellationToken);
            }

            return response;
        }
    }
}
