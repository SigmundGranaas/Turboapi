using Turboapi.Activities.BackcountrySki.domain;
using Turboapi.Activities.BackcountrySki.value;
using Turboapi.Activities.value;

namespace Turboapi.Activities.BackcountrySki.conditions;

/// <summary>
/// Default backcountry ski advisor. Pulls weather for the route's
/// midpoint (chosen as the most representative single sample of an
/// alpine route) and combines it with the activity's typed
/// <see cref="BackcountrySkiDetails"/>:
///
/// * heavy precipitation in the last 6h → fresh snow but unstable
/// * strong wind at the ridge → loading, scour, poor visibility
/// * pressure dropping fast → incoming weather; avoid committing
/// * ATES rating Complex on a high-danger day → severe penalty
///   (currently inert until the Varsom provider arrives — the
///   AvalancheLevel field is null and the scoring just notes it).
/// </summary>
public sealed class BackcountrySkiConditionsAdvisor : IBackcountrySkiConditionsAdvisor
{
    private readonly IWeatherProvider _weather;
    private readonly TimeProvider _clock;

    public BackcountrySkiConditionsAdvisor(IWeatherProvider weather, TimeProvider? clock = null)
    {
        _weather = weather;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<BackcountrySkiConditionsReport> AdviseAsync(
        BackcountrySkiActivity activity, DateTimeOffset at, CancellationToken cancellationToken)
    {
        // Use the route midpoint — better representative for a multi-km
        // alpine route than the start or end.
        var line = activity.Route;
        var midpoint = line.Coordinates[line.NumPoints / 2];

        var weather = await _weather.GetAsync(midpoint.Y, midpoint.X, at, cancellationToken);

        var (score, rationale) = ScoreAndRationale(weather, activity.Details);

        return new BackcountrySkiConditionsReport(
            activityId: activity.Core.Id,
            validAt: weather.ValidAt,
            fetchedAt: _clock.GetUtcNow(),
            weather: weather,
            avalancheLevel: null,
            avalancheSummary: null,
            score: score,
            rationale: rationale);
    }

    private static (int? score, string rationale) ScoreAndRationale(
        WeatherSlice w, BackcountrySkiDetails d)
    {
        var s = 100.0;
        var reasons = new List<string>();

        // Fresh-snow window: 6h precip is the proxy for new accumulation,
        // good in the abstract but the wind regime turns it into hazard.
        var precip6h = w.PrecipitationNext6hMm ?? 0;
        if (precip6h > 10 && w.WindSpeedMs > 12)
        {
            s -= 35;
            reasons.Add($"{precip6h:F0}mm/6h + strong wind: wind loading on lee aspects");
        }
        else if (precip6h > 10)
        {
            reasons.Add($"fresh snow ({precip6h:F0}mm/6h)");
        }

        // Ridge wind.
        if (w.WindSpeedMs > 18)
        {
            s -= 30;
            reasons.Add($"ridge wind {w.WindSpeedMs:F0} m/s — exposed ridges dangerous");
        }
        else if (w.WindSpeedMs > 12)
        {
            s -= 15;
            reasons.Add($"fresh wind ({w.WindSpeedMs:F0} m/s)");
        }

        // Freeze line + temperature regime is approximated from air temp;
        // a real product would derive freezing level from the upper-air
        // forecast. Above freezing in winter → wet snow concerns.
        if (w.AirTemperatureCelsius > 3)
        {
            s -= 15;
            reasons.Add($"{w.AirTemperatureCelsius:F0}°C — wet snow / glide");
        }

        if (w.AirPressureHpa < 990)
        {
            s -= 10;
            reasons.Add("very low pressure — system moving through");
        }

        // ATES rating × (placeholder avalanche level): no real avalanche
        // data yet, but we note the user's preferred max so the rationale
        // surfaces the gap when the provider lands.
        if (d.PreferredAvalancheMaxLevel is { } maxLevel && maxLevel <= 2 && d.AtesRating == AtesRating.Complex)
        {
            reasons.Add("complex terrain; avalanche level data not yet available — verify Varsom before going");
        }

        var score = (int)Math.Clamp(Math.Round(s), 0, 100);
        var rationale = reasons.Count == 0
            ? "Stable weather; check Varsom for the avalanche bulletin before heading out."
            : $"{string.Join(". ", reasons)}. Check Varsom for the avalanche bulletin before heading out.";

        return (score == 100 && reasons.Count == 0 ? (int?)null : score, rationale);
    }
}
