using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;
using TrafficEvents.Simulator;

SimulatorOptions options;
try
{
    options = SimulatorOptions.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
if (options.Duration > TimeSpan.Zero)
{
    cts.CancelAfter(options.Duration);
}

using var http = new HttpClient { BaseAddress = options.Url, Timeout = TimeSpan.FromSeconds(10) };
http.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);

var model = new TrafficModel(options);
var stats = new ConcurrentDictionary<string, long>();
using var gate = new SemaphoreSlim(options.Concurrency);
var inFlight = new ConcurrentDictionary<Task, byte>();

Console.WriteLine($"Sending ~{options.Rate}/s to {options.Url} in batches of {options.BatchSize} " +
                  $"for {(options.Duration > TimeSpan.Zero ? options.Duration.TotalSeconds + "s" : "ever")}");

// One tick per batch: rate 100/s with batch 10 → a request every 100 ms.
var period = TimeSpan.FromSeconds(options.BatchSize / options.Rate);
using var timer = new PeriodicTimer(period);
var clock = Stopwatch.StartNew();
var lastReport = TimeSpan.Zero;

try
{
    while (await timer.WaitForNextTickAsync(cts.Token))
    {
        var batch = new JsonArray();
        for (var i = 0; i < options.BatchSize; i++)
        {
            if (model.ShouldMalform())
            {
                // Malformed events go alone: a batch is all-or-nothing, and we don't want to sink good ones.
                Send(new JsonArray(model.Malformed()), "malformed");
            }
            else
            {
                batch.Add(model.Next());
            }
        }

        Send(batch, "batch");

        if (clock.Elapsed - lastReport >= TimeSpan.FromSeconds(5))
        {
            lastReport = clock.Elapsed;
            Report();
        }
    }
}
catch (OperationCanceledException)
{
    // Duration elapsed or Ctrl+C.
}

await Task.WhenAll(inFlight.Keys);
Report();
return stats.GetValueOrDefault("error") > 0 ? 1 : 0;

void Send(JsonArray body, string kind)
{
    if (body.Count == 0)
    {
        return;
    }

    if (!gate.Wait(0))
    {
        // Back-pressure: the target can't keep up at this concurrency; count it rather than queue forever.
        stats.AddOrUpdate("skipped_backpressure", body.Count, (_, v) => v + body.Count);
        return;
    }

    var task = Task.Run(async () =>
    {
        try
        {
            using var response = await http.PostAsJsonAsync("/events", body);
            var key = $"{(int)response.StatusCode}";
            stats.AddOrUpdate(key, body.Count, (_, v) => v + body.Count);
            if (kind == "malformed" && (int)response.StatusCode != 400)
            {
                stats.AddOrUpdate("malformed_not_rejected", 1, (_, v) => v + 1);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            stats.AddOrUpdate("error", body.Count, (_, v) => v + body.Count);
        }
        finally
        {
            gate.Release();
        }
    });
    inFlight.TryAdd(task, 0);
    task.ContinueWith(t => inFlight.TryRemove(t, out _), TaskScheduler.Default);
}

void Report()
{
    var parts = stats.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}");
    Console.WriteLine($"[{clock.Elapsed:mm\\:ss}] events by HTTP status: {string.Join(" ", parts)}");
}
