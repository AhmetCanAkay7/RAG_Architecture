namespace SK_UserGuide.Services.Abstract;

public interface IRagService
{
    /// <summary>
    /// Ask a question with optional session for chat history.
    /// </summary>
    Task<string> AskAsync(string question, string? sessionId = null);

    Task AddDocumentAsync(string text, string id);

    /// <summary>
    /// Clear chat history for a session.
    /// </summary>
    void ClearChatHistory(string sessionId);
}
