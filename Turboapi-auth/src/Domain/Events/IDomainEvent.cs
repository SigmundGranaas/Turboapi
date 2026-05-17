namespace Turboapi.Domain.Events
{
    public interface IDomainEvent : Turbo.Messaging.IDomainEvent
    {
        // Marker interface for domain events.
        // Inherits from the cross-module Turbo.Messaging.IDomainEvent so that
        // every Auth domain event participates in the unified event surface.
    }
}