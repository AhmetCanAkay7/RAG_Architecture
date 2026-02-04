using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using SK_UserGuide.Configuration;
using SK_UserGuide.Services.Abstract;
using SK_UserGuide.Services.LLM;
using SK_UserGuide.Services.Retrieval;

namespace SK_UserGuide.Services.Concrete;

/// <summary>
/// RAG retrieval service with streaming responses.
/// Supports: Hybrid mode (LLM + context) and ContextOnly mode (no LLM).
/// Optimized for Qwen3:4b model.
/// </summary>
public class RagRetrievalService : IRagRetrievalService
{
    private readonly IHybridRetrievalService _hybridRetrieval;
    private readonly Kernel _kernel;
    private readonly ContextCompressor _compressor;
    private readonly PromptBuilder _promptBuilder;
    private readonly string _responseMode;

    private const string ChatSystemPrompt = "You are a helpful assistant. Respond briefly and professionally in English.";

    public RagRetrievalService(
        IHybridRetrievalService hybridRetrieval,
        Kernel kernel,
        IOptions<RagSettings> ragSettings)
    {
        _hybridRetrieval = hybridRetrieval;
        _kernel = kernel;
        _compressor = new ContextCompressor();
        _promptBuilder = new PromptBuilder();
        _responseMode = ragSettings.Value.ResponseMode;
    }

    /// <summary>
    /// Process a question with streaming response.
    /// </summary>
    public async IAsyncEnumerable<string> AskStreamingAsync(
        string question,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 1. Get context from vector DB
        var retrievalResult = await _hybridRetrieval.RetrieveAsync(question);

        // 2. No context → General chat (streaming)
        if (retrievalResult.ChunkCount == 0)
        {
            var chatPrompt = $"{ChatSystemPrompt}\n\nUser: {question}\nAssistant:";
            await foreach (var chunk in StreamLLMAsync(chatPrompt, cancellationToken))
            {
                yield return chunk;
            }
            yield break;
        }

        // 3. ContextOnly mode → Return context directly (no LLM)
        if (_responseMode == "ContextOnly")
        {
            yield return FormatContextOnly(question, retrievalResult);
            yield break;
        }

        // 4. Hybrid mode → Stream LLM response with sources
        var compressedContext = _compressor.Compress(
            retrievalResult.SelectedChunks,
            question,
            "EN");

        var prompt = _promptBuilder.Build(question, compressedContext);

        await foreach (var chunk in StreamLLMAsync(prompt, cancellationToken))
        {
            yield return chunk;
        }

        // Append sources at the end
        var sources = FormatSources(compressedContext);
        if (!string.IsNullOrEmpty(sources))
        {
            yield return sources;
        }
    }

    /// <summary>
    /// Stream LLM response token by token.
    /// </summary>
    private async IAsyncEnumerable<string> StreamLLMAsync(
        string prompt,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var settings = new OpenAIPromptExecutionSettings
        {
            MaxTokens = 200,        // Shorter = faster
            Temperature = 0.1,      // Lower = more deterministic
            TopP = 0.85
        };

        await foreach (var chunk in _kernel.InvokePromptStreamingAsync(prompt, new KernelArguments(settings), cancellationToken: cancellationToken))
        {
            var text = chunk.ToString();
            if (!string.IsNullOrEmpty(text))
            {
                yield return text;
            }
        }
    }

    private string FormatContextOnly(string question, RetrievalResult retrievalResult)
    {
        var compressedContext = _compressor.Compress(
            retrievalResult.SelectedChunks,
            question,
            "EN");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(compressedContext.Summary);
        sb.Append(FormatSources(compressedContext));
        return sb.ToString();
    }

    /// <summary>
    /// Format sources for display.
    /// </summary>
    private static string FormatSources(CompressedContext context)
    {
        if (context.Items.Count == 0)
            return string.Empty;

        var sourceLines = context.Items
            .Select(i =>
            {
                var parts = new List<string> { $"[{i.Index}] {i.DocName ?? "Unknown"}" };
                if (i.Page.HasValue)
                    parts.Add($"Page {i.Page}");
                if (!string.IsNullOrEmpty(i.Section))
                    parts.Add(i.Section);
                return string.Join(" - ", parts);
            });

        return "\n\n---\n📚 **Sources:**\n" + string.Join("\n", sourceLines);
    }
}
