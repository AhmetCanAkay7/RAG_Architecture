using SK_UserGuide.Services.Abstract;
using SK_UserGuide.Services.Chat;

namespace SK_UserGuide.Services.Concrete;

public class RagService : IRagService
{
    private readonly RagRetrievalService _retrievalService;
    private readonly RagIngestionService _ingestionService;
    private readonly ChatHistoryManager _chatHistory;
    private readonly ResponseCache _responseCache;

    public RagService(
        RagRetrievalService retrievalService,
        RagIngestionService ingestionService,
        ChatHistoryManager chatHistory,
        ResponseCache responseCache)
    {
        _retrievalService = retrievalService;
        _ingestionService = ingestionService;
        _chatHistory = chatHistory;
        _responseCache = responseCache;
    }

    public async Task<string> AskAsync(string question, string? sessionId = null)
    {
        // 1. Check cache first (skip all processing if hit)
        if (_responseCache.TryGet(question, out var cachedAnswer) && cachedAnswer != null)
        {
            // Still add to history for context continuity
            if (!string.IsNullOrEmpty(sessionId))
            {
                _chatHistory.AddUserMessage(sessionId, question);
                _chatHistory.AddAssistantMessage(sessionId, cachedAnswer);
            }
            return cachedAnswer + "\n\n_(cached response)_";
        }

        // 2. Get conversation context if session exists
        string? conversationContext = null;
        if (!string.IsNullOrEmpty(sessionId))
        {
            conversationContext = _chatHistory.GetConversationContext(sessionId);
            _chatHistory.AddUserMessage(sessionId, question);
        }

        // 3. Full RAG pipeline
        var answer = await _retrievalService.AskAsync(question, conversationContext);

        // 4. Cache the response (only if no session context to keep it generic)
        if (string.IsNullOrEmpty(conversationContext))
        {
            _responseCache.Set(question, answer);
        }

        // 5. Store assistant response in history
        if (!string.IsNullOrEmpty(sessionId))
        {
            _chatHistory.AddAssistantMessage(sessionId, answer);
        }

        return answer;
    }

    public void ClearChatHistory(string sessionId)
    {
        _chatHistory.ClearSession(sessionId);
    }

    public void ClearResponseCache()
    {
        _responseCache.Clear();
    }

    public async Task AddDocumentAsync(string text, string baseId)
    {
        // Invalidate cache when documents change
        _responseCache.Clear();

        var metadata = new Dictionary<string, object>
        {
            ["title"] = "User Guide",
            ["path"] = baseId,
            ["source_type"] = "uploaded"
        };
        await _ingestionService.IngestDocumentAsync(baseId, text, metadata);
    }
}
