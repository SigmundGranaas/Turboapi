using System.Diagnostics;

namespace Turboapi_geo.infrastructure;

public abstract class ResilientBackgroundService : BackgroundService
{
    private readonly ILogger _logger;
    private readonly ActivitySource _activitySource;

    protected ResilientBackgroundService(
        ILogger logger,
        string activitySourceName)
    {
        _logger = logger;
        _activitySource = new ActivitySource(activitySourceName);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessMessagesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in background service. Retrying in 5s");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    protected abstract Task ProcessMessagesAsync(CancellationToken stoppingToken);
    
    protected Activity? StartActivity(string name) => _activitySource.StartActivity(name);
}