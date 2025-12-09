namespace SK_UserGuide.Services.Abstract;

public interface IRagService
{
    Task<string> AskAsync(string question); // ask a question.
    Task AddDocumentAsync(string text, string id);
}
