using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Turbo.Messaging;
using Turbo.Outbox;
using Turbo.Outbox.Postgres;
using Turboapi.Activities.conditions;
using Turboapi.Activities.controller;
using Turboapi.Activities.data;
using Turboapi.Activities.domain.services;
using Turboapi.Activities.events;
using Turboapi.Activities.services;
using Turboapi.Activities.value;

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

        // Conditions cache + weather provider chain. Composition: per-kind
        // advisors take an IWeatherProvider; the registration here picks
        // SyntheticWeatherProvider out of the box, swapping in
        // MetNoWeatherProvider when MetNo:UserAgent is set in
        // configuration. Either way the consumer is wrapped in
        // CachedWeatherProvider so all kinds share one Postgres-backed
        // cache.
        services.AddScoped<IConditionsCache, PgConditionsCache>();
        services.Configure<MetNoOptions>(configuration.GetSection("MetNo"));

        var metNoUserAgent = configuration["MetNo:UserAgent"];
        if (!string.IsNullOrWhiteSpace(metNoUserAgent))
        {
            services.AddHttpClient(MetNoWeatherProvider.HttpClientName, (sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<MetNoOptions>>().Value;
                http.BaseAddress = new Uri(opts.BaseUrl);
                http.DefaultRequestHeaders.UserAgent.ParseAdd(opts.UserAgent!);
                http.Timeout = TimeSpan.FromSeconds(10);
            });
            services.AddScoped<MetNoWeatherProvider>();
            services.AddScoped<IWeatherProvider>(sp => new CachedWeatherProvider(
                sp.GetRequiredService<MetNoWeatherProvider>(),
                sp.GetRequiredService<IConditionsCache>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CachedWeatherProvider>>()));
        }
        else
        {
            services.AddSingleton<SyntheticWeatherProvider>();
            services.AddScoped<IWeatherProvider>(sp => new CachedWeatherProvider(
                sp.GetRequiredService<SyntheticWeatherProvider>(),
                sp.GetRequiredService<IConditionsCache>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CachedWeatherProvider>>()));
        }

        // Avalanche provider. Synthetic by default; VarsomAvalancheProvider
        // takes over when Varsom:Enabled=true is set.
        services.Configure<VarsomOptions>(configuration.GetSection("Varsom"));
        if (configuration.GetValue<bool>("Varsom:Enabled"))
        {
            services.AddHttpClient(VarsomAvalancheProvider.HttpClientName, (sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<VarsomOptions>>().Value;
                http.BaseAddress = new Uri(opts.BaseUrl);
                http.Timeout = TimeSpan.FromSeconds(10);
            });
            services.AddScoped<VarsomAvalancheProvider>();
            services.AddScoped<IAvalancheProvider>(sp => new CachedAvalancheProvider(
                sp.GetRequiredService<VarsomAvalancheProvider>(),
                sp.GetRequiredService<IConditionsCache>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CachedAvalancheProvider>>()));
        }
        else
        {
            services.AddSingleton<SyntheticAvalancheProvider>();
            services.AddScoped<IAvalancheProvider>(sp => new CachedAvalancheProvider(
                sp.GetRequiredService<SyntheticAvalancheProvider>(),
                sp.GetRequiredService<IConditionsCache>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CachedAvalancheProvider>>()));
        }

        // River flow provider. Synthetic by default; NveRiverFlowProvider
        // takes over when Nve:ApiKey is set.
        services.Configure<NveOptions>(configuration.GetSection("Nve"));
        var nveApiKey = configuration["Nve:ApiKey"];
        if (!string.IsNullOrWhiteSpace(nveApiKey))
        {
            services.AddHttpClient(NveRiverFlowProvider.HttpClientName, (sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<NveOptions>>().Value;
                http.BaseAddress = new Uri(opts.BaseUrl);
                http.DefaultRequestHeaders.Add("X-API-Key", opts.ApiKey!);
                http.Timeout = TimeSpan.FromSeconds(10);
            });
            services.AddScoped<NveRiverFlowProvider>();
            services.AddScoped<IRiverFlowProvider>(sp => new CachedRiverFlowProvider(
                sp.GetRequiredService<NveRiverFlowProvider>(),
                sp.GetRequiredService<IConditionsCache>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CachedRiverFlowProvider>>()));
        }
        else
        {
            services.AddSingleton<SyntheticRiverFlowProvider>();
            services.AddScoped<IRiverFlowProvider>(sp => new CachedRiverFlowProvider(
                sp.GetRequiredService<SyntheticRiverFlowProvider>(),
                sp.GetRequiredService<IConditionsCache>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CachedRiverFlowProvider>>()));
        }

        // Tide provider. Synthetic by default; SehavnivaTideProvider
        // takes over when Sehavniva:Enabled=true is set.
        services.Configure<SehavnivaOptions>(configuration.GetSection("Sehavniva"));
        if (configuration.GetValue<bool>("Sehavniva:Enabled"))
        {
            services.AddHttpClient(SehavnivaTideProvider.HttpClientName, (sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<SehavnivaOptions>>().Value;
                http.BaseAddress = new Uri(opts.BaseUrl);
                http.Timeout = TimeSpan.FromSeconds(10);
            });
            services.AddScoped<SehavnivaTideProvider>();
            services.AddScoped<ITideProvider>(sp => new CachedTideProvider(
                sp.GetRequiredService<SehavnivaTideProvider>(),
                sp.GetRequiredService<IConditionsCache>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CachedTideProvider>>()));
        }
        else
        {
            services.AddSingleton<SyntheticTideProvider>();
            services.AddScoped<ITideProvider>(sp => new CachedTideProvider(
                sp.GetRequiredService<SyntheticTideProvider>(),
                sp.GetRequiredService<IConditionsCache>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CachedTideProvider>>()));
        }

        // Grooming provider. Synthetic by default; SkisporetGroomingProvider
        // takes over when Skisporet:Enabled=true is set.
        services.Configure<SkisporetOptions>(configuration.GetSection("Skisporet"));
        if (configuration.GetValue<bool>("Skisporet:Enabled"))
        {
            services.AddHttpClient(SkisporetGroomingProvider.HttpClientName, (sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<SkisporetOptions>>().Value;
                http.BaseAddress = new Uri(opts.BaseUrl);
                http.Timeout = TimeSpan.FromSeconds(10);
            });
            services.AddScoped<SkisporetGroomingProvider>();
            services.AddScoped<IGroomingProvider>(sp => new CachedGroomingProvider(
                sp.GetRequiredService<SkisporetGroomingProvider>(),
                sp.GetRequiredService<IConditionsCache>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CachedGroomingProvider>>()));
        }
        else
        {
            services.AddSingleton<SyntheticGroomingProvider>();
            services.AddScoped<IGroomingProvider>(sp => new CachedGroomingProvider(
                sp.GetRequiredService<SyntheticGroomingProvider>(),
                sp.GetRequiredService<IConditionsCache>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CachedGroomingProvider>>()));
        }

        // Optional background warmer for the conditions cache. Off by
        // default (ConditionsCacheWarmer:Enabled=true to switch on).
        services.Configure<ConditionsCacheWarmerOptions>(configuration.GetSection("ConditionsCacheWarmer"));
        services.AddHostedService<ConditionsCacheWarmerHostedService>();

        services.AddControllers().AddApplicationPart(typeof(ActivitySummariesController).Assembly);

        return services;
    }

    private static string ResolveConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStringName} is not configured");
}
