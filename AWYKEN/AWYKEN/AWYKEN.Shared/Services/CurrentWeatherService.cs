using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace AWYKEN.Shared.Services;

public interface ICurrentWeatherService
{
    Task<WeatherInfo> GetCurrentAsync(double latitude, double longitude, CancellationToken cancellationToken = default);
}

/// <summary>Fetches current conditions for saved home coordinates from Open-Meteo.</summary>
public sealed class OpenMeteoWeatherService(HttpClient httpClient) : ICurrentWeatherService
{
    public async Task<WeatherInfo> GetCurrentAsync(
        double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        // Open-Meteo accepts global coordinates and requires no key for non-commercial development use.
        var url = string.Create(CultureInfo.InvariantCulture,
            $"https://api.open-meteo.com/v1/forecast?latitude={latitude:R}&longitude={longitude:R}&current=temperature_2m,weather_code&temperature_unit=celsius&timezone=auto");
        var response = await httpClient.GetFromJsonAsync<OpenMeteoResponse>(url, cancellationToken);
        if (response?.Current?.Temperature is not double temperature)
            return new WeatherInfo(null, "Weather unavailable · Open-Meteo");

        var description = Describe(response.Current.WeatherCode);
        return new WeatherInfo((int)Math.Round(temperature), $"{description} · Open-Meteo");
    }

    private static string Describe(int? code) => code switch
    {
        0 => "Clear sky",
        1 => "Mainly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle",
        56 or 57 => "Freezing drizzle",
        61 or 63 or 65 => "Rain",
        66 or 67 => "Freezing rain",
        71 or 73 or 75 or 77 => "Snow",
        80 or 81 or 82 => "Rain showers",
        85 or 86 => "Snow showers",
        95 or 96 or 99 => "Thunderstorm",
        _ => "Current conditions"
    };

    private sealed class OpenMeteoResponse
    {
        [JsonPropertyName("current")] public CurrentConditions? Current { get; set; }
    }

    private sealed class CurrentConditions
    {
        [JsonPropertyName("temperature_2m")] public double? Temperature { get; set; }
        [JsonPropertyName("weather_code")] public int? WeatherCode { get; set; }
    }
}
