using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Turboapi.Activities.value;

namespace Turboapi.Activities.conditions;

/// <summary>
/// Sehavnivå (api.sehavniva.no, Kartverket) tide provider. Fetches a
/// short window of observed + forecast water levels around the
/// requested instant and projects them into the typed
/// <see cref="TideSlice"/>.
///
/// Sehavnivå returns XML; we parse the minimum we need (timeseries
/// data points) by hand to avoid pulling in an XML schema. If the
/// upstream contract changes we replace the parser, not the interface.
///
/// Wiring: registered only when <c>Sehavniva:Enabled=true</c>;
/// otherwise <see cref="SyntheticTideProvider"/> is wired in its
/// place.
/// </summary>
public sealed class SehavnivaTideProvider : ITideProvider
{
    public const string HttpClientName = "sehavniva";

    public string Key => "sehavniva_tide";

    private readonly IHttpClientFactory _http;
    private readonly ILogger<SehavnivaTideProvider> _logger;

    public SehavnivaTideProvider(IHttpClientFactory http, ILogger<SehavnivaTideProvider> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<TideSlice> GetAsync(
        double latitude, double longitude, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var client = _http.CreateClient(HttpClientName);
        var from = at.UtcDateTime.AddMinutes(-15).ToString("yyyy-MM-ddTHH:mm");
        var to = at.UtcDateTime.AddMinutes(60).ToString("yyyy-MM-ddTHH:mm");

        // tideapi.php returns observed + forecast water levels relative
        // to several datums. We request "all" data sources and pick the
        // one closest to "now".
        var url = $"tideapi.php?lat={Math.Round(latitude, 4)}&lon={Math.Round(longitude, 4)}"
                  + $"&fromtime={Uri.EscapeDataString(from)}&totime={Uri.EscapeDataString(to)}"
                  + $"&datatype=all&refcode=cd&place=&file=&lang=en&interval=10&dst=0&tzone=&tide_request=locationdata";

        var xml = await client.GetStringAsync(url, cancellationToken);
        if (string.IsNullOrWhiteSpace(xml))
            throw new InvalidOperationException("Sehavnivå returned empty body");

        // Parse out the two waterlevel datapoints nearest the requested
        // instant. Sehavnivå emits <waterlevel value="X" time="Y" .../>
        // entries; we look for the time + value attributes by string
        // scan to avoid a heavyweight XML reader.
        var points = ParseWaterlevels(xml).ToList();
        if (points.Count == 0)
            throw new InvalidOperationException("Sehavnivå returned no waterlevel entries");

        // Closest to `at` for "current", and one ~15min later for trend.
        points.Sort((a, b) => a.Time.CompareTo(b.Time));
        var current = points.OrderBy(p => Math.Abs((p.Time - at).TotalSeconds)).First();
        var aheadIdx = points.FindIndex(p => p.Time > current.Time);

        string summary;
        if (aheadIdx < 0) summary = "current level unchanged";
        else
        {
            var ahead = points[aheadIdx];
            var dh = ahead.Value - current.Value;
            summary = Math.Abs(dh) < 0.02
                ? "slack"
                : dh > 0 ? "rising tide" : "falling tide";
        }

        return new TideSlice(
            validAt: current.Time,
            currentHeightMeters: current.Value,
            summary: summary);
    }

    private static IEnumerable<(DateTimeOffset Time, float Value)> ParseWaterlevels(string xml)
    {
        // Tiny string scanner. Each entry looks like:
        //   <waterlevel value="0.123" time="2026-05-20T12:00:00+02:00" ...
        // (attribute order may vary; we extract by name).
        var idx = 0;
        while ((idx = xml.IndexOf("<waterlevel", idx, StringComparison.Ordinal)) >= 0)
        {
            var end = xml.IndexOf('>', idx);
            if (end < 0) yield break;
            var fragment = xml.AsSpan(idx, end - idx);
            var value = ExtractAttribute(fragment, "value");
            var time = ExtractAttribute(fragment, "time");
            if (value is not null && time is not null
                && float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
                && DateTimeOffset.TryParse(time, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var t))
            {
                yield return (t, v);
            }
            idx = end + 1;
        }
    }

    private static string? ExtractAttribute(ReadOnlySpan<char> fragment, string name)
    {
        // Look for `name="..."`
        var needle = name + "=\"";
        var start = fragment.IndexOf(needle, StringComparison.Ordinal);
        if (start < 0) return null;
        start += needle.Length;
        var endQuote = fragment.Slice(start).IndexOf('"');
        if (endQuote < 0) return null;
        return fragment.Slice(start, endQuote).ToString();
    }
}

public sealed class SehavnivaOptions
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://api.sehavniva.no/";
}
