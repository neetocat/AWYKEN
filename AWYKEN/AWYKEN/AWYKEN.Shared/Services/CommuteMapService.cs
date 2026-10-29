using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace AWYKEN.Shared.Services;

public interface ICommuteMapService
{
    Task<MapCoordinate?> GeocodeAddressAsync(string address, CancellationToken cancellationToken = default);
    Task<DrivingRoute> GetDrivingRouteAsync(MapCoordinate home, MapCoordinate school, CancellationToken cancellationToken = default);
}

public sealed record MapCoordinate(double Latitude, double Longitude);

public sealed record DrivingRoute(double DistanceMeters, double DurationSeconds, IReadOnlyList<MapCoordinate> Coordinates);

/// <summary>
/// Uses OpenStreetMap Nominatim for explicit address lookups and OSRM for a driving route.
/// </summary>
public sealed class OpenStreetMapCommuteService(HttpClient httpClient) : ICommuteMapService
{
    private static readonly SemaphoreSlim GeocodeRateLimit = new(1, 1);
    private static DateTime _lastGeocodeAtUtc = DateTime.MinValue;

    public async Task<MapCoordinate?> GeocodeAddressAsync(
        string address, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address))
            return null;

        // Nominatim's public endpoint allows at most one request per second; serialize lookups.
        await GeocodeRateLimit.WaitAsync(cancellationToken);
        try
        {
            var delay = TimeSpan.FromSeconds(1) - (DateTime.UtcNow - _lastGeocodeAtUtc);
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);

            _lastGeocodeAtUtc = DateTime.UtcNow;
            var url = "https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&q="
                + Uri.EscapeDataString(address.Trim());
            var results = await httpClient.GetFromJsonAsync<List<NominatimResult>>(url, cancellationToken);

            var first = results?.FirstOrDefault();
            if (first is null
                || !double.TryParse(first.Latitude, CultureInfo.InvariantCulture, out var latitude)
                || !double.TryParse(first.Longitude, CultureInfo.InvariantCulture, out var longitude))
                return null;

            return new MapCoordinate(latitude, longitude);
        }
        finally
        {
            GeocodeRateLimit.Release();
        }
    }

    public async Task<DrivingRoute> GetDrivingRouteAsync(
        MapCoordinate home, MapCoordinate school, CancellationToken cancellationToken = default)
    {
        // OSRM expects longitude,latitude order even though Leaflet markers use latitude,longitude.
        var url = string.Create(CultureInfo.InvariantCulture,
            $"https://router.project-osrm.org/route/v1/driving/{home.Longitude},{home.Latitude};{school.Longitude},{school.Latitude}?overview=full&geometries=geojson");
        var response = await httpClient.GetFromJsonAsync<OsrmResponse>(url, cancellationToken);
        var route = response?.Routes?.FirstOrDefault();
        if (response?.Code != "Ok" || route is null || route.Geometry?.Coordinates is null)
            throw new InvalidOperationException("No drivable route was found between those locations.");

        var points = route.Geometry.Coordinates
            .Where(point => point.Length >= 2)
            .Select(point => new MapCoordinate(point[1], point[0]))
            .ToList();
        return new DrivingRoute(route.Distance, route.Duration, points);
    }

    private sealed class NominatimResult
    {
        [JsonPropertyName("lat")] public string Latitude { get; set; } = "";
        [JsonPropertyName("lon")] public string Longitude { get; set; } = "";
    }

    private sealed class OsrmResponse
    {
        [JsonPropertyName("code")] public string? Code { get; set; }
        [JsonPropertyName("routes")] public List<OsrmRoute>? Routes { get; set; }
    }

    private sealed class OsrmRoute
    {
        [JsonPropertyName("distance")] public double Distance { get; set; }
        [JsonPropertyName("duration")] public double Duration { get; set; }
        [JsonPropertyName("geometry")] public OsrmGeometry? Geometry { get; set; }
    }

    private sealed class OsrmGeometry
    {
        [JsonPropertyName("coordinates")] public List<double[]>? Coordinates { get; set; }
    }
}
