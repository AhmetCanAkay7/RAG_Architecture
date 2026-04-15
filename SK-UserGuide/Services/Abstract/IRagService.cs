namespace SK_UserGuide.Services.Abstract;

public interface IRagService
{
    /// <summary>
    /// Ask a question using RAG pipeline with streaming response.
    /// </summary>
    /// <param name="question">User question.</param>
    /// <param name="tenantId">Tenant identifier for collection routing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<string> AskStreamingAsync(string question, string tenantId, CancellationToken cancellationToken = default);
}
