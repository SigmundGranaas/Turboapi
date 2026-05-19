using Microsoft.EntityFrameworkCore;
using Turbo.Messaging;
using Turbo.Outbox;
using Turbo.Outbox.Postgres;
using Turboapi.Activities.BackcountrySki.controller;
using Turboapi.Activities.BackcountrySki.data;
using Turboapi.Activities.BackcountrySki.domain.handler;
using Turboapi.Activities.BackcountrySki.events;
using Turboapi.Activities.domain.services;
using Turboapi.Activities.value;

namespace Turboapi.Activities.BackcountrySki;

/// <summary>
/// Composition entry point for the Backcountry Ski activity kind. Same
/// shape as the Fishing module: dedicated EF context, command + event
/// handlers, read-side reader, outbox dispatcher, and a single
/// <see cref="ActivityKindDescriptor"/> contributed to the shared catalog.
/// </summary>
public static class BackcountrySkiActivityModule
{
    public const string ConnectionStringName = "ActivitiesBackcountrySki";

    public static IServiceCollection AddBackcountrySkiActivityModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = ResolveConnectionString(configuration);

        services.AddDbContext<BackcountrySkiContext>((sp, options) =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.UseNetTopologySuite();
                npgsql.EnableRetryOnFailure();
            }));

        services.AddScoped<IBackcountrySkiActivityReader, EfBackcountrySkiActivityReader>();

        services.AddScoped<CreateBackcountrySkiActivityHandler>();
        services.AddScoped<UpdateBackcountrySkiActivityHandler>();
        services.AddScoped<DeleteBackcountrySkiActivityHandler>();

        services.AddScoped<BackcountrySkiActivityCreatedHandler>();
        services.AddScoped<BackcountrySkiActivityUpdatedHandler>();
        services.AddScoped<BackcountrySkiActivityDeletedHandler>();
        services.AddScoped<IEventHandler<BackcountrySkiActivityCreated>>(sp =>
            sp.GetRequiredService<BackcountrySkiActivityCreatedHandler>());
        services.AddScoped<IEventHandler<BackcountrySkiActivityUpdated>>(sp =>
            sp.GetRequiredService<BackcountrySkiActivityUpdatedHandler>());
        services.AddScoped<IEventHandler<BackcountrySkiActivityDeleted>>(sp =>
            sp.GetRequiredService<BackcountrySkiActivityDeletedHandler>());

        services.AddScoped<IOutbox<BackcountrySkiScope>, PgOutbox<BackcountrySkiContext, BackcountrySkiScope>>();
        services.AddScoped<IUnitOfWork<BackcountrySkiScope>, PgUnitOfWork<BackcountrySkiContext, BackcountrySkiScope>>();
        services.AddScoped<IIdempotencyStore<BackcountrySkiContext>, PgIdempotencyStore<BackcountrySkiContext>>();
        services.AddHostedService<OutboxDispatcherHostedService<BackcountrySkiContext>>();

        services.AddSingleton(new ActivityKindDescriptor
        {
            Key = "backcountry_ski",
            DisplayName = "Backcountry skiing",
            IconKey = "backcountry_ski",
            ColorHex = "#7A3CCB",
            AllowedGeometries = new HashSet<ActivityGeometryKind> { ActivityGeometryKind.LineString },
            ConditionsAvailable = false,
        });

        services.AddControllers().AddApplicationPart(typeof(BackcountrySkiActivitiesController).Assembly);

        return services;
    }

    private static string ResolveConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStringName} is not configured");
}
