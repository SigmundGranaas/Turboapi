using Microsoft.EntityFrameworkCore;
using Turbo.Messaging;
using Turbo.Outbox;
using Turbo.Outbox.Postgres;
using Turboapi.Activities.domain.services;
using Turboapi.Activities.Fishing.conditions;
using Turboapi.Activities.Fishing.controller;
using Turboapi.Activities.Fishing.data;
using Turboapi.Activities.Fishing.domain.handler;
using Turboapi.Activities.Fishing.events;
using Turboapi.Activities.value;

namespace Turboapi.Activities.Fishing;

/// <summary>
/// Composition entry point for the Fishing activity kind. Wires its
/// dedicated EF context (with its own outbox + processed-events tables),
/// command + event handlers, the read-side reader implementation, and
/// contributes its <see cref="ActivityKindDescriptor"/> to the shared
/// kind catalog.
/// </summary>
public static class FishingActivityModule
{
    public const string ConnectionStringName = "ActivitiesFishing";

    public static IServiceCollection AddFishingActivityModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = ResolveConnectionString(configuration);

        services.AddDbContext<FishingContext>((sp, options) =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.UseNetTopologySuite();
                npgsql.EnableRetryOnFailure();
            }));

        services.AddScoped<IFishingActivityReader, EfFishingActivityReader>();

        services.AddScoped<CreateFishingActivityHandler>();
        services.AddScoped<UpdateFishingActivityHandler>();
        services.AddScoped<DeleteFishingActivityHandler>();

        services.AddScoped<FishingActivityCreatedHandler>();
        services.AddScoped<FishingActivityUpdatedHandler>();
        services.AddScoped<FishingActivityDeletedHandler>();
        services.AddScoped<IEventHandler<FishingActivityCreated>>(sp =>
            sp.GetRequiredService<FishingActivityCreatedHandler>());
        services.AddScoped<IEventHandler<FishingActivityUpdated>>(sp =>
            sp.GetRequiredService<FishingActivityUpdatedHandler>());
        services.AddScoped<IEventHandler<FishingActivityDeleted>>(sp =>
            sp.GetRequiredService<FishingActivityDeletedHandler>());

        services.AddScoped<IOutbox<FishingScope>, PgOutbox<FishingContext, FishingScope>>();
        services.AddScoped<IUnitOfWork<FishingScope>, PgUnitOfWork<FishingContext, FishingScope>>();
        services.AddScoped<IIdempotencyStore<FishingContext>, PgIdempotencyStore<FishingContext>>();
        services.AddHostedService<OutboxDispatcherHostedService<FishingContext>>();

        // Fishing conditions advisor. Composes the IWeatherProvider that
        // the shared module registered (synthetic by default, met.no when
        // MetNo:UserAgent is configured). Tides + river-flow advisors
        // will compose alongside this in follow-ups.
        services.AddScoped<IFishingConditionsAdvisor, FishingConditionsAdvisor>();

        // Contribute the kind descriptor to the shared catalog. Composition:
        // the catalog discovers kinds via DI rather than a hardcoded enum.
        services.AddSingleton(new ActivityKindDescriptor
        {
            Key = "fishing",
            DisplayName = "Fishing",
            IconKey = "fishing",
            ColorHex = "#1E6FB8",
            AllowedGeometries = new HashSet<ActivityGeometryKind> { ActivityGeometryKind.Point },
            ConditionsAvailable = true,
        });

        services.AddControllers().AddApplicationPart(typeof(FishingActivitiesController).Assembly);

        return services;
    }

    private static string ResolveConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStringName} is not configured");
}
