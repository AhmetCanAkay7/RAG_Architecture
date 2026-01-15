using SK_UserGuide.Services.Abstract;
using SK_UserGuide.Services.Chat;

namespace SK_UserGuide.Services.Concrete;

/// <summary>
/// RAG service with response caching.
/// Delegates to IRagRetrievalService for actual RAG processing.
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

    /// <summary>
    /// Ask a question using RAG pipeline with response caching.
    /// </summary>
    public async Task<string> AskAsync(string question)
    {
        // Check cache first
        if (_responseCache.TryGet(question, out var cachedAnswer))
        {
            return cachedAnswer!;
        }

        // Process with RAG pipeline
        var answer = await _ragRetrievalService.AskAsync(question);

        // Cache the response
        _responseCache.Set(question, answer);

        return answer;
    }

    /// <summary>
    /// Ask a question using RAG pipeline with streaming response.
    /// If cached, yields complete response immediately.
    /// Otherwise, streams response and caches after completion.
    /// </summary>
    public async IAsyncEnumerable<string> AskStreamingAsync(
        string question,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Check cache first - if cached, yield complete response immediately
        if (_responseCache.TryGet(question, out var cachedAnswer))
        {
            yield return cachedAnswer!;
            yield break;
        }

        // Stream from RAG pipeline and accumulate for caching
        var fullResponse = new System.Text.StringBuilder();

        await foreach (var chunk in _ragRetrievalService.AskStreamingAsync(question, cancellationToken))
        {
            fullResponse.Append(chunk);
            yield return chunk;
        }

        // Cache the complete response for future requests
        var completeResponse = fullResponse.ToString();
        if (!string.IsNullOrEmpty(completeResponse))
        {
            _responseCache.Set(question, completeResponse);
        }
    }
}

