using Microsoft.EntityFrameworkCore;
using Turbo.Messaging;
using Turbo.Outbox;
using Turbo.Outbox.Postgres;
using Turboapi.Activities.controller;
using Turboapi.Activities.data;
using Turboapi.Activities.domain.services;
using Turboapi.Activities.events;
using Turboapi.Activities.services;

namespace Turboapi.Activities;

/// <summary>
/// Composition entry point for the Activities shared module. Owns the
/// cross-kind summaries database, the kind catalog facade, the shared
/// domain services (geometry normalisation, owner guard), and the summary
/// projection subscribers. Per-kind modules (e.g. AddFishingActivityModule)
/// are registered separately by the host.
/// </summary>
public static class ActivitiesSharedModule
{
    public const string ConnectionStringName = "Activities";

    public static IServiceCollection AddActivitiesSharedModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = ResolveConnectionString(configuration);

        services.AddDbContext<ActivitySummariesContext>((sp, options) =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.UseNetTopologySuite();
                npgsql.EnableRetryOnFailure();
            }));

        // Shared domain services. Composition root: every kind handler picks
        // these up from DI rather than inheriting any base class.
        services.AddSingleton<IGeometryNormalizer, GeometryNormalizer>();
        services.AddSingleton<IOwnerGuard, OwnerGuard>();
        services.AddSingleton<IActivityKindCatalog, InMemoryActivityKindCatalog>();

        // The summaries projection is its own outbox/UnitOfWork scope so the
        // projector can write to the summaries table + processed-events
        // atomically. Kind modules have their own scopes.
        services.AddScoped<IOutbox<ActivitiesScope>, PgOutbox<ActivitySummariesContext, ActivitiesScope>>();
        services.AddScoped<IUnitOfWork<ActivitiesScope>, PgUnitOfWork<ActivitySummariesContext, ActivitiesScope>>();
        services.AddScoped<IIdempotencyStore<ActivitySummariesContext>, PgIdempotencyStore<ActivitySummariesContext>>();

        services.AddScoped<ActivitySummaryUpsertedHandler>();
        services.AddScoped<ActivitySummaryDeletedHandler>();
        services.AddScoped<IEventHandler<ActivitySummaryUpserted>>(sp =>
            sp.GetRequiredService<ActivitySummaryUpsertedHandler>());
        services.AddScoped<IEventHandler<ActivitySummaryDeleted>>(sp =>
            sp.GetRequiredService<ActivitySummaryDeletedHandler>());

        services.AddHostedService<OutboxDispatcherHostedService<ActivitySummariesContext>>();

        services.AddControllers().AddApplicationPart(typeof(ActivitySummariesController).Assembly);

        return services;
    }

    private static string ResolveConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStringName} is not configured");
}
