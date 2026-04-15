namespace SK_UserGuide.Services.Abstract;

/// <summary>
/// Interface for RAG retrieval service with streaming responses.
/// </summary>
public interface IRagRetrievalService
{
    /// <summary>
    /// Process a question with RAG pipeline using streaming response.
    /// </summary>
    /// <param name="question">User question.</param>
    /// <param name="tenantId">Tenant identifier for collection routing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<string> AskStreamingAsync(string question, string tenantId, CancellationToken cancellationToken = default);
}
