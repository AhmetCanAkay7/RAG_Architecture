using SK_UserGuide.Services.Abstract;
using SK_UserGuide.Services.Chat;

namespace SK_UserGuide.Services.Concrete;

/// <summary>
/// RAG service with response caching and streaming.
/// Routes requests through the RAG pipeline with tenant isolation.
/// </summary>
public class RagService : IRagService
{
    private readonly IRagRetrievalService _ragRetrievalService;
    private readonly ResponseCache _responseCache;

    public RagService(
        IRagRetrievalService ragRetrievalService,
        ResponseCache responseCache)
    {
        _ragRetrievalService = ragRetrievalService;
        _responseCache = responseCache;
    }

    public async IAsyncEnumerable<string> AskStreamingAsync(
        string question,
        string tenantId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Build cache key with tenant isolation
        var cacheKey = $"{tenantId}::{question}";

        // Check cache first
        if (_responseCache.TryGet(cacheKey, out var cachedAnswer))
        {
            yield return cachedAnswer!;
            yield break;
        }

        var fullResponse = new System.Text.StringBuilder();

        await foreach (var chunk in _ragRetrievalService.AskStreamingAsync(question, tenantId, cancellationToken))
        {
            fullResponse.Append(chunk);
            yield return chunk;
        }

        // Cache the complete response
        var completeResponse = fullResponse.ToString();
        if (!string.IsNullOrEmpty(completeResponse))
        {
            _responseCache.Set(cacheKey, completeResponse);
        }
    }
}
