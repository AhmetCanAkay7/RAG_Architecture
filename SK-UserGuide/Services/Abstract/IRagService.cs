namespace SK_UserGuide.Services.Abstract;

public interface IRagService
{
    /// <summary>
    /// Ask a question using RAG pipeline with response caching.
    /// </summary>
    Task<string> AskAsync(string question);
}
