using Microsoft.Extensions.Caching.Memory;
using MongoDB.Driver;
using TrafficEvents.Infrastructure.Mongo;

namespace TrafficEvents.Processor;

/// <summary>Junction reference data for enrichment, cached in memory (it changes rarely).</summary>
public sealed class JunctionDirectory(TrafficCollections collections, IMemoryCache cache)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

    public async Task<JunctionDocument?> FindAsync(string junctionId, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue<JunctionDocument>(CacheKey(junctionId), out var cached))
        {
            return cached;
        }

        var junction = await collections.Junctions.Find(j => j.Id == junctionId).FirstOrDefaultAsync(cancellationToken);
        if (junction is not null)
        {
            // Unknown junctions are not cached, so newly added ones are picked up immediately.
            cache.Set(CacheKey(junctionId), junction, CacheFor);
        }

        return junction;
    }

    private static string CacheKey(string junctionId) => $"junction:{junctionId}";
}
