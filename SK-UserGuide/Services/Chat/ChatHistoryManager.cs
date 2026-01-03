using System.Collections.Concurrent;
using Microsoft.SemanticKernel.ChatCompletion;

namespace SK_UserGuide.Services.Chat;

/// <summary>
/// Simple in-memory chat history manager.
/// Stores conversation history per session with automatic cleanup.
/// </summary>
public class ChatHistoryManager
{
    private readonly ConcurrentDictionary<string, ChatSession> _sessions = new();
    private readonly int _maxMessagesPerSession;
    private readonly TimeSpan _sessionTimeout;

    public ChatHistoryManager(int maxMessagesPerSession = 10, int sessionTimeoutMinutes = 30)
    {
        _maxMessagesPerSession = maxMessagesPerSession;
        _sessionTimeout = TimeSpan.FromMinutes(sessionTimeoutMinutes);
    }

    /// <summary>
    /// Get or create a chat session.
    /// </summary>
    public ChatHistory GetOrCreateHistory(string sessionId)
    {
        CleanupExpiredSessions();

        var session = _sessions.GetOrAdd(sessionId, _ => new ChatSession
        {
            History = new ChatHistory(),
            LastAccess = DateTime.UtcNow
        });

        session.LastAccess = DateTime.UtcNow;
        return session.History;
    }

    /// <summary>
    /// Add a user message to the session.
    /// </summary>
    public void AddUserMessage(string sessionId, string message)
    {
        var history = GetOrCreateHistory(sessionId);
        history.AddUserMessage(message);
        TrimHistoryIfNeeded(history);
    }

    /// <summary>
    /// Add an assistant message to the session.
    /// </summary>
    public void AddAssistantMessage(string sessionId, string message)
    {
        var history = GetOrCreateHistory(sessionId);
        history.AddAssistantMessage(message);
        TrimHistoryIfNeeded(history);
    }

    /// <summary>
    /// Get the last N messages as context string for RAG prompt.
    /// </summary>
    public string GetConversationContext(string sessionId, int lastNMessages = 4)
    {
        var history = GetOrCreateHistory(sessionId);

        if (history.Count == 0)
            return string.Empty;

        var messages = history.TakeLast(lastNMessages).ToList();

        if (messages.Count == 0)
            return string.Empty;

        var context = new System.Text.StringBuilder();
        context.AppendLine("Previous conversation:");

        foreach (var msg in messages)
        {
            var role = msg.Role == AuthorRole.User ? "User" : "Assistant";
            // Truncate long messages for context
            var content = msg.Content?.Length > 200
                ? msg.Content[..200] + "..."
                : msg.Content;
            context.AppendLine($"{role}: {content}");
        }

        return context.ToString();
    }

    /// <summary>
    /// Clear a specific session.
    /// </summary>
    public void ClearSession(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
    }

    /// <summary>
    /// Keep only the last N messages to prevent memory bloat.
    /// </summary>
    private void TrimHistoryIfNeeded(ChatHistory history)
    {
        while (history.Count > _maxMessagesPerSession * 2) // Keep pairs (user + assistant)
        {
            history.RemoveAt(0);
        }
    }

    /// <summary>
    /// Remove sessions that haven't been accessed recently.
    /// </summary>
    private void CleanupExpiredSessions()
    {
        var now = DateTime.UtcNow;
        var expiredKeys = _sessions
            .Where(kvp => now - kvp.Value.LastAccess > _sessionTimeout)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _sessions.TryRemove(key, out _);
        }
    }

    private class ChatSession
    {
        public required ChatHistory History { get; init; }
        public DateTime LastAccess { get; set; }
    }
}
