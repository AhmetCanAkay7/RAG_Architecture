namespace SK_UserGuide.Services.Abstract;

/// <summary>
/// Interface for RAG service with caching and streaming.
/// </summary>
public interface IRagService
{
    /// <summary>
    /// Ask a question using RAG pipeline with streaming response.
    /// Cached responses are yielded immediately.
    /// </summary>
    IAsyncEnumerable<string> AskStreamingAsync(string question, CancellationToken cancellationToken = default);
}
