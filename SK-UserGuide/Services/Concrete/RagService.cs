using SK_UserGuide.Services.Abstract;

namespace SK_UserGuide.Services.Concrete;

/// <summary>
/// RAG service with streaming.
/// Routes requests through the RAG pipeline with tenant isolation.
/// </summary>
public class RagService : IRagService
{
    private readonly IRagRetrievalService _ragRetrievalService;

    public RagService(
        IRagRetrievalService ragRetrievalService)
    {
        _ragRetrievalService = ragRetrievalService;
    }

    public async IAsyncEnumerable<string> AskStreamingAsync(
        string question,
        string tenantId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in _ragRetrievalService.AskStreamingAsync(question, tenantId, cancellationToken))
        {
            yield return chunk;
        }
    }
}
