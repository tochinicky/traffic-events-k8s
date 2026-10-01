using System.Globalization;

namespace TrafficEvents.Simulator;

public sealed record SimulatorOptions
{
    public Uri Url { get; init; } = new("http://localhost:30080");
    public string ApiKey { get; init; } = Environment.GetEnvironmentVariable("INGEST_API_KEY") ?? string.Empty;
    public double Rate { get; init; } = 20;            // events per second
    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(60); // zero = run until Ctrl+C
    public int BatchSize { get; init; } = 10;
    public int Concurrency { get; init; } = 4;
    public int Junctions { get; init; } = 20;
    public double DuplicateRatio { get; init; } = 0.05;
    public double MalformedRatio { get; init; } = 0.01;
    public double IncidentRatio { get; init; } = 0.02;
    public double PoisonRatio { get; init; }            // valid at ingest, unknown junction → DLQ
    public int? Seed { get; init; }

    public const string Usage = """
        Usage: simulator [options]
          --url <url>             ingest-api base URL           (default http://localhost:30080)
          --api-key <key>         X-Api-Key (or env INGEST_API_KEY)
          --rate <n>              events per second             (default 20)
          --duration <seconds>    0 = until Ctrl+C              (default 60)
          --batch <n>             events per request, 1-100     (default 10)
          --concurrency <n>       parallel requests             (default 4)
          --junctions <n>         junctions J-1..J-n            (default 20)
          --duplicates <ratio>    resent events                 (default 0.05)
          --malformed <ratio>     invalid events (expect 400)   (default 0.01)
          --incidents <ratio>     incident events               (default 0.02)
          --poison <ratio>        unknown-junction events (end in the DLQ) (default 0)
          --seed <n>              deterministic run
        """;

    public static SimulatorOptions Parse(string[] args)
    {
        var o = new SimulatorOptions();
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {args[i]}");
            double D() => double.Parse(Next(), CultureInfo.InvariantCulture);
            int I() => int.Parse(Next(), CultureInfo.InvariantCulture);

            o = args[i] switch
            {
                "--url" => o with { Url = new Uri(Next()) },
                "--api-key" => o with { ApiKey = Next() },
                "--rate" => o with { Rate = D() },
                "--duration" => o with { Duration = TimeSpan.FromSeconds(D()) },
                "--batch" => o with { BatchSize = Math.Clamp(I(), 1, 100) },
                "--concurrency" => o with { Concurrency = Math.Max(1, I()) },
                "--junctions" => o with { Junctions = Math.Max(1, I()) },
                "--duplicates" => o with { DuplicateRatio = D() },
                "--malformed" => o with { MalformedRatio = D() },
                "--incidents" => o with { IncidentRatio = D() },
                "--poison" => o with { PoisonRatio = D() },
                "--seed" => o with { Seed = I() },
                "-h" or "--help" => throw new ArgumentException(Usage),
                _ => throw new ArgumentException($"Unknown option {args[i]}\n{Usage}"),
            };
        }

        return o;
    }
}
