using Turboapi.Application.Interfaces;
using Turboapi.Application.Results;

namespace Turboapi.Application.Behaviors
{
    public class UnitOfWorkCommandHandlerDecorator<TCommand, TResponse> : ICommandHandler<TCommand, TResponse>
    {
        private readonly ICommandHandler<TCommand, TResponse> _decorated;
        private readonly IUnitOfWork _unitOfWork;

        public UnitOfWorkCommandHandlerDecorator(
            ICommandHandler<TCommand, TResponse> decorated,
            IUnitOfWork unitOfWork)
        {
            _decorated = decorated;
            _unitOfWork = unitOfWork;
        }

        public async Task<TResponse> Handle(TCommand command, CancellationToken cancellationToken)
        {
            var response = await _decorated.Handle(command, cancellationToken);
            
            // Check if the response is a successful Result before saving
            if (response is Results.IResult { IsSuccess: true })
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return response;
        }
    }
}