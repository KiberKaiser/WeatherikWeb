using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net.Http;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();
var app = builder.Build();

app.UseDefaultFiles(); 
app.UseStaticFiles();

app.MapGet("/api/search", async (string query, IHttpClientFactory httpClientFactory) =>
{
    var client = httpClientFactory.CreateClient();
    var url = $"https://geocoding-api.open-meteo.com/v1/search?name={query}&count=5&language=uk&format=json";
    var response = await client.GetStringAsync(url);
    var json = JsonDocument.Parse(response);

    if (!json.RootElement.TryGetProperty("results", out var results))
        return Results.NotFound("Населені пункти не знайдено");

    var locations = results.EnumerateArray().Select(r => new
    {
        name = r.GetProperty("name").GetString(),
        latitude = r.GetProperty("latitude").GetDouble(),
        longitude = r.GetProperty("longitude").GetDouble(),
        country = r.GetProperty("country").GetString(),
        admin1 = r.TryGetProperty("admin1", out var region) ? region.GetString() : null
    });

    return Results.Ok(locations);
});

app.MapGet("/api/forecast", async (double lat, double lon, IHttpClientFactory httpClientFactory) =>
{
    var client = httpClientFactory.CreateClient();
    var url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&hourly=temperature_2m,weathercode&timezone=auto";
    var response = await client.GetStringAsync(url);
    var json = JsonDocument.Parse(response);

    var hourly = json.RootElement.GetProperty("hourly");
    var times = hourly.GetProperty("time").EnumerateArray().Select(e => DateTime.Parse(e.GetString()!)).ToList();
    var temps = hourly.GetProperty("temperature_2m").EnumerateArray().ToList();
    var codes = hourly.GetProperty("weathercode").EnumerateArray().ToList();

    string GetWeatherDescription(int code) => code switch
    {
        0 or 1 or 2 => "Ясно",
        3 or 4 or 5 => "Мінлива хмарність",
        45 or 48 => "Туман",
        51 or 53 or 55 => "Мряка",
        61 or 63 or 65 => "Дощ",
        71 or 73 or 75 => "Сніг",
        80 or 81 or 82 => "Зливи",
        95 => "Гроза",
        _ => "Невідома погода"
    };

    var forecast = times
        .Select((time, i) => new { time, temp = temps[i].GetDouble(), code = codes[i].GetInt32() })
        .GroupBy(x => x.time.Date)
        .Select(dayGroup => new
        {
            date = dayGroup.Key.ToString("yyyy-MM-dd"),
            morning = GetPeriod(dayGroup, 6, 9),
            day = GetPeriod(dayGroup, 12, 15),
            evening = GetPeriod(dayGroup, 18, 21),
            night = GetPeriod(dayGroup, 0, 3)
        });

    object? GetPeriod(IEnumerable<dynamic> dayGroup, int fromHour, int toHour)
    {
        var period = dayGroup.FirstOrDefault(x => x.time.Hour >= fromHour && x.time.Hour <= toHour);
        return period != null ? new
        {
            temperature = period.temp,
            weather = GetWeatherDescription(period.code)
        } : null;
    }

    return Results.Ok(forecast);
});

app.Run();
