namespace SK_UserGuide.Services.Abstract;

/// <summary>
/// Interface for RAG retrieval service with context compression and structured responses.
/// </summary>
public interface IRagRetrievalService
{
    /// <summary>
    /// Process a question with RAG pipeline, optionally including conversation history.
    /// </summary>
    Task<string> AskAsync(string question, string? conversationContext = null);
}
