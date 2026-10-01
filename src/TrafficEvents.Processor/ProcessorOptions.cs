namespace TrafficEvents.Processor;

public sealed class ProcessorOptions
{
    public const string SectionName = "Processor";

    /// <summary>A measurement slower than this raises a low_speed alert.</summary>
    public double LowSpeedThresholdKmh { get; set; } = 20;
}
