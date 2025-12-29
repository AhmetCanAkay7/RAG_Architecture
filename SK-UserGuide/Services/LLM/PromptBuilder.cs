using System.Text;

namespace SK_UserGuide.Services.LLM;

/// <summary>
/// Builds prompts with simplified system prompt and formatted context.
/// </summary>
public class PromptBuilder
{
    // Ultra-simplified prompt for small models (mistral:7b, phi3)
    private const string SystemPrompt = @"Sen bir kurumsal bilgi asistanısın. SADECE verilen bağlamı kullan. Kaynak belirt: [1], [2]. Kısa ve net yanıtla.";

    /// <summary>
    /// Build the complete prompt for LLM.
    /// </summary>
    public string Build(string question, CompressedContext context)
    {
        var formattedContext = FormatContext(context);

        return $@"{SystemPrompt}

BAĞLAM:
{formattedContext}

SORU: {question}

CEVAP:";
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
