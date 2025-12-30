using System.Text;

namespace SK_UserGuide.Services.LLM;

/// <summary>
/// Builds prompts with simplified system prompt and formatted context.
/// </summary>
public class PromptBuilder
{
    // Simplified English prompt for optimal LLM performance
    private const string SystemPrompt = @"You are an enterprise knowledge assistant. Use ONLY the provided context. Cite sources: [1], [2]. Respond concisely.";

    /// <summary>
    /// Build the complete prompt for LLM.
    /// </summary>
    public string Build(string question, CompressedContext context)
    {
        var formattedContext = FormatContext(context);

        return $@"{SystemPrompt}

CONTEXT:
{formattedContext}

QUESTION: {question}

ANSWER:";
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
