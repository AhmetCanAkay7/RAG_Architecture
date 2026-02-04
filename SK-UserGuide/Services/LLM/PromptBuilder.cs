using System.Text;

namespace SK_UserGuide.Services.LLM;

/// <summary>
/// Builds prompts with simplified system prompt and formatted context.
/// </summary>
public class PromptBuilder
{
    // Simplified English prompt for optimal LLM performance
    private const string SystemPrompt = @"Use only the context below and answer in English breifly!";

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
        sb.AppendLine("\n\n");

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
    /// Simple context format for faster processing.
    /// </summary>
    private string FormatContext(CompressedContext context)
    {
        var sb = new StringBuilder();
        foreach (var item in context.Items)
        {
            sb.AppendLine($"[{item.Index}] {item.Text}");
        }
        return sb.ToString().Trim();
    }
}
