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
}
