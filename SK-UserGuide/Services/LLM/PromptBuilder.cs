using System.Text;

namespace SK_UserGuide.Services.LLM;

/// <summary>
/// Builds prompts with detailed system instructions and formatted context.
/// Enhanced for Azure OpenAI's larger context window capability.
/// </summary>
public class PromptBuilder
{
    private const string SystemPrompt = @"You are a knowledgeable assistant for internal company documentation.
Your role is to answer questions accurately using ONLY the provided context.

RULES:
1. Answer ONLY based on the provided context. Do not use prior knowledge or make assumptions.
2. If the context does not contain enough information to answer, clearly state: ""The provided documents do not contain sufficient information to answer this question.""
3. Be thorough but concise. Use bullet points or numbered lists when listing multiple items.
4. Cite your sources using [1], [2] etc. notation that corresponds to the context indices.
5. Respond in the same language as the question.
6. If the question is ambiguous, provide the most relevant interpretation based on available context.
7. Format your response using markdown for better readability (bold for key terms, code blocks for technical content).
8. When describing step-by-step processes, preserve the original order from the documentation.";

    /// <summary>
    /// Build the complete prompt for LLM.
    /// </summary>
    public string Build(string question, CompressedContext context)
    {
        return BuildWithHistory(question, context, null);
    }   

    /// <summary>
    /// Build the complete prompt for LLM with optional chat history.
    /// </summary>
    public string BuildWithHistory(string question, CompressedContext context, string? conversationHistory = null)
    {
        var formattedContext = FormatContext(context);
        var sb = new StringBuilder();

        sb.AppendLine(SystemPrompt);
        sb.AppendLine();

        // Include conversation history if available
        if (!string.IsNullOrEmpty(conversationHistory))
        {
            sb.AppendLine("CONVERSATION HISTORY:");
            sb.AppendLine(conversationHistory);
            sb.AppendLine();
        }

        sb.AppendLine("CONTEXT:");
        sb.AppendLine(formattedContext);
        sb.AppendLine();    
        sb.AppendLine($"QUESTION: {question}");
        sb.AppendLine();
        sb.Append("ANSWER:");

        return sb.ToString();
    }

    /// <summary>
    /// Context format with source metadata for better LLM grounding.
    /// </summary>
    private string FormatContext(CompressedContext context)
    {
        var sb = new StringBuilder();
        foreach (var item in context.Items)
        {
            var meta = new List<string>();
            if (!string.IsNullOrEmpty(item.DocName)) meta.Add($"Source: {item.DocName}");
            if (item.Page.HasValue) meta.Add($"Page: {item.Page}");
            if (!string.IsNullOrEmpty(item.Section)) meta.Add($"Section: {item.Section}");

            var metaStr = meta.Count > 0 ? $" ({string.Join(", ", meta)})" : "";
            sb.AppendLine($"[{item.Index}]{metaStr}");
            sb.AppendLine(item.Text);
            sb.AppendLine();
        }
        return sb.ToString().Trim();
    }
}
