namespace SK_UserGuide.Services.Abstract;

public interface IRagService
{

    Task InitAsync(); // loads datas to the memory.
    Task<string> AskAsync(string question); // ask a question.
}
