using Turboapi.Domain.Events; // For IDomainEvent

namespace Turboapi.Application.Interfaces
{
    public interface IEventPublisher
    {
        Task PublishAsync<TEvent>(TEvent domainEvent) where TEvent : IDomainEvent;
    }
}