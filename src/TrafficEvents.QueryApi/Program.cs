using TrafficEvents.Infrastructure.Hosting;
using TrafficEvents.Infrastructure.Mongo;
using TrafficEvents.QueryApi;

const string ServiceName = "query-api";
const int MaxMinutes = 24 * 60;
const int MaxLimit = 500;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults(ServiceName);
builder.Services.AddMongo(builder.Configuration);
builder.Services.AddSingleton<TrafficQueries>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();

var app = builder.Build();
app.MapServiceDefaults(ServiceName);

app.MapGet("/junctions/{id}/status", async (string id, TrafficQueries queries, CancellationToken ct) =>
    await queries.GetStatusAsync(id, ct) is { } status
        ? Results.Ok(status)
        : Results.Problem(statusCode: 404, title: $"No status for junction '{id}' yet."));

app.MapGet("/incidents", async (DateTime? since, int? limit, TrafficQueries queries, TimeProvider time, CancellationToken ct) =>
{
    var take = limit ?? 100;
    if (take is < 1 or > MaxLimit)
    {
        return Results.Problem(statusCode: 400, title: $"limit must be 1-{MaxLimit}.");
    }

    var from = since?.ToUniversalTime() ?? time.GetUtcNow().UtcDateTime.AddHours(-1);
    return Results.Ok(await queries.GetIncidentsAsync(from, take, ct));
});

app.MapGet("/junctions/{id}/speed", async (string id, int? minutes, TrafficQueries queries, TimeProvider time, CancellationToken ct) =>
{
    var window = minutes ?? 15;
    if (window is < 1 or > MaxMinutes)
    {
        return Results.Problem(statusCode: 400, title: $"minutes must be 1-{MaxMinutes}.");
    }

    if (!await queries.JunctionExistsAsync(id, ct))
    {
        return Results.Problem(statusCode: 404, title: $"Unknown junction '{id}'.");
    }

    return Results.Ok(await queries.GetSpeedAsync(id, window, time.GetUtcNow().UtcDateTime, ct));
});

app.Run();
