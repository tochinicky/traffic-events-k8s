namespace TrafficEvents.IngestApi;

public sealed class IngestOptions
{
    public const string SectionName = "Ingest";
    public const string ApiKeyHeader = "X-Api-Key";

    /// <summary>From the Kubernetes Secret. Empty means "reject everything", never "allow everything".</summary>
    public string ApiKey { get; set; } = string.Empty;

    public int MaxBatchSize { get; set; } = 100;
}
