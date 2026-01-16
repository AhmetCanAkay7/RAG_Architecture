using System.Collections.Concurrent;

namespace SK_UserGuide.Services.Chat;
public class ResponseCache
{
    private readonly ConcurrentDictionary<string, CachedResponse> _cache = new();
    private readonly TimeSpan _entryLifetime;
    private readonly int _maxEntries;

    public ResponseCache(int maxEntries = 100, int lifetimeMinutes = 60)
    {
        _maxEntries = maxEntries;
        _entryLifetime = TimeSpan.FromMinutes(lifetimeMinutes);
    }

    /// <summary>
    /// Try to get a cached response for the question.
    /// </summary>
    public bool TryGet(string question, out string? answer)
    {
        answer = null;
        var key = NormalizeKey(question);

        if (_cache.TryGetValue(key, out var cached))
        {
            // Check if entry is still valid
            if (DateTime.UtcNow - cached.CreatedAt < _entryLifetime)
            {
                cached.HitCount++;
                answer = cached.Answer;
                return true;
            }

            // Expired - remove it
            _cache.TryRemove(key, out _);
        }

        return false;
    }

    /// <summary>
    /// Store a response in the cache.
    /// </summary>
    public void Set(string question, string answer)
    {
        var key = NormalizeKey(question);

        // Evict oldest entries if at capacity
        if (_cache.Count >= _maxEntries)
        {
            EvictOldest();
        }

        _cache[key] = new CachedResponse
        {
            Answer = answer,
            CreatedAt = DateTime.UtcNow,
            HitCount = 0
        };
    }

    /// <summary>
    /// Invalidate all cache entries (e.g., after document update).
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Get cache statistics.
    /// </summary>
    public CacheStats GetStats()
    {
        var now = DateTime.UtcNow;
        var validEntries = _cache.Values.Count(c => now - c.CreatedAt < _entryLifetime);
        var totalHits = _cache.Values.Sum(c => c.HitCount);

        return new CacheStats
        {
            TotalEntries = _cache.Count,
            ValidEntries = validEntries,
            TotalHits = totalHits
        };
    }

    /// <summary>
    /// Normalize question to cache key (lowercase, trimmed).
    /// </summary>
    private string NormalizeKey(string question)
    {
        return question.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Remove oldest entries when at capacity.
    /// </summary>
    private void EvictOldest()
    {
        var oldest = _cache
            .OrderBy(kvp => kvp.Value.CreatedAt)
            .Take(_cache.Count / 4) // Remove 25% of oldest
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in oldest)
        {
            _cache.TryRemove(key, out _);
        }
    }

    private class CachedResponse
    {
        public required string Answer { get; init; }
        public DateTime CreatedAt { get; init; }
        public int HitCount { get; set; }
    }
}

public record CacheStats
{
    public int TotalEntries { get; init; }
    public int ValidEntries { get; init; }
    public int TotalHits { get; init; }
}
