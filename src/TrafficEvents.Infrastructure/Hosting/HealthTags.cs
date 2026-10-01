namespace TrafficEvents.Infrastructure.Hosting;

public static class HealthTags
{
    /// <summary>Checks that gate traffic: dependencies reachable. Liveness runs no checks at all.</summary>
    public const string Ready = "ready";
}
