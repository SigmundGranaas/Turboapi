using Microsoft.Extensions.DependencyInjection;
using Turbo.Messaging.InProcess;
using Turboapi.Activities.BackcountrySki.events;
using Turboapi.Activities.events;
using Turboapi.Activities.Fishing.events;
using Turboapi.Collections.domain.events;
using Turboapi.Geo.domain.events;
using Turboapi.Tracks.domain.events;

namespace Turbo.Host.Modulith;

/// <summary>
/// Single source of truth for which event subjects the modulith host
/// dispatches in-process. <c>Program.cs</c> calls this; the
/// <c>SubscriberCoverage</c> architecture test reads the same list to
/// assert every IDomainEvent in the modules has either a registered
/// subscriber here or sits on the explicit audit-only allowlist.
///
/// New event added to a module? Add an AddInProcessSubscriber line
/// here OR mark it audit-only in
/// <see cref="AuditOnlyEvents"/>. CI will tell you when you've
/// forgotten.
/// </summary>
public static class SubscriberWiring
{
    /// <summary>
    /// Event types intentionally published to the outbox (for external
    /// auditors / future consumers) but with no in-process subscriber
    /// inside the modulith. Anything not in this set must have an
    /// AddInProcessSubscriber call below.
    /// </summary>
    public static readonly IReadOnlySet<Type> AuditOnlyEvents = new HashSet<Type>
    {
        // Every Auth event is audit-only inside the modulith — Auth has
        // no internal projection subscriber. External consumers attach to
        // turbo.auth.> on the JetStream stream when running as
        // microservices.
        typeof(Turboapi.Auth.Domain.Events.AccountCreatedEvent),
        typeof(Turboapi.Auth.Domain.Events.AccountLoggedInEvent),
        typeof(Turboapi.Auth.Domain.Events.AccountLastLoginUpdatedEvent),
        typeof(Turboapi.Auth.Domain.Events.RoleAddedToAccountEvent),
        typeof(Turboapi.Auth.Domain.Events.PasswordAuthMethodAddedEvent),
        typeof(Turboapi.Auth.Domain.Events.OAuthAuthMethodAddedEvent),
        typeof(Turboapi.Auth.Domain.Events.RefreshTokenGeneratedEvent),
        typeof(Turboapi.Auth.Domain.Events.RefreshTokenRevokedEvent),
        typeof(Turboapi.Auth.Domain.Events.SuspiciousRefreshTokenAttemptEvent),
    };

    public static IServiceCollection AddTurboInProcessSubscribers(this IServiceCollection services)
    {
        services.AddInProcessSubscriber<LocationCreated>("turbo.geo.LocationCreated");
        services.AddInProcessSubscriber<LocationUpdated>("turbo.geo.LocationUpdated");
        services.AddInProcessSubscriber<LocationDeleted>("turbo.geo.LocationDeleted");
        services.AddInProcessSubscriber<TrackCreated>("turbo.tracks.TrackCreated");
        services.AddInProcessSubscriber<TrackUpdated>("turbo.tracks.TrackUpdated");
        services.AddInProcessSubscriber<TrackDeleted>("turbo.tracks.TrackDeleted");
        services.AddInProcessSubscriber<CollectionCreated>("turbo.collections.CollectionCreated");
        services.AddInProcessSubscriber<CollectionUpdated>("turbo.collections.CollectionUpdated");
        services.AddInProcessSubscriber<CollectionDeleted>("turbo.collections.CollectionDeleted");
        services.AddInProcessSubscriber<CollectionItemAdded>("turbo.collections.CollectionItemAdded");
        services.AddInProcessSubscriber<CollectionItemRemoved>("turbo.collections.CollectionItemRemoved");

        // Fishing activity kind. Two consumers per kind: the typed read-model
        // projector (FishingActivity*Handler) and the shared cross-kind
        // summaries projector (ActivitySummary*Handler). Both consume events
        // off the fishing outbox under the turbo.activities.fishing.* subject.
        services.AddInProcessSubscriber<FishingActivityCreated>("turbo.activities.fishing.FishingActivityCreated");
        services.AddInProcessSubscriber<FishingActivityUpdated>("turbo.activities.fishing.FishingActivityUpdated");
        services.AddInProcessSubscriber<FishingActivityDeleted>("turbo.activities.fishing.FishingActivityDeleted");
        services.AddInProcessSubscriber<ActivitySummaryUpserted>("turbo.activities.fishing.ActivitySummaryUpserted");
        services.AddInProcessSubscriber<ActivitySummaryDeleted>("turbo.activities.fishing.ActivitySummaryDeleted");

        // Backcountry ski activity kind — same two-consumer shape:
        // typed projector for backcountry_ski.activities + the shared
        // summaries projector for the cross-kind read model.
        services.AddInProcessSubscriber<BackcountrySkiActivityCreated>("turbo.activities.backcountry_ski.BackcountrySkiActivityCreated");
        services.AddInProcessSubscriber<BackcountrySkiActivityUpdated>("turbo.activities.backcountry_ski.BackcountrySkiActivityUpdated");
        services.AddInProcessSubscriber<BackcountrySkiActivityDeleted>("turbo.activities.backcountry_ski.BackcountrySkiActivityDeleted");
        services.AddInProcessSubscriber<ActivitySummaryUpserted>("turbo.activities.backcountry_ski.ActivitySummaryUpserted");
        services.AddInProcessSubscriber<ActivitySummaryDeleted>("turbo.activities.backcountry_ski.ActivitySummaryDeleted");

        return services;
    }
}
