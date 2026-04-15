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
/// LLM parameters are configurable via LlmSettings.
/// </summary>
public class RagRetrievalService : IRagRetrievalService
{
    private readonly IHybridRetrievalService _hybridRetrieval;
    private readonly Kernel _kernel;
    private readonly PromptBuilder _promptBuilder;
    private readonly LlmSettings _llmSettings;
    private readonly QdrantSettings _qdrantSettings;
    private readonly string _responseMode;

    private const string ChatSystemPrompt =
        "You are a helpful assistant. Respond professionally and clearly. " +
        "If you don't know the answer, say so honestly.";

    public RagRetrievalService(
        IHybridRetrievalService hybridRetrieval,
        Kernel kernel,
        IOptions<RagSettings> ragSettings,
        IOptions<LlmSettings> llmSettings,
        IOptions<QdrantSettings> qdrantSettings,
        PromptBuilder promptBuilder)
    {
        _hybridRetrieval = hybridRetrieval;
        _kernel = kernel;
        _promptBuilder = promptBuilder;
        _llmSettings = llmSettings.Value;
        _qdrantSettings = qdrantSettings.Value;
        _responseMode = ragSettings.Value.ResponseMode;
    }

    /// <summary>
    /// Process a question with streaming response for a specific tenant.
    /// </summary>
    public async IAsyncEnumerable<string> AskStreamingAsync(
        string question,
        string tenantId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var collectionName = ResolveCollectionName(tenantId);

        // 1. Get context from vector DB
        var retrievalResult = await _hybridRetrieval.RetrieveAsync(question, collectionName);

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
            yield return FormatContextOnly(retrievalResult);
            yield break;
        }

        // 4. Hybrid mode → Stream LLM response with sources
        var prompt = _promptBuilder.Build(question, retrievalResult.SelectedChunks);

        await foreach (var chunk in StreamLLMAsync(prompt, cancellationToken))
        {
            yield return chunk;
        }

        // Append sources at the end
        var sources = FormatSources(retrievalResult.SelectedChunks);
        if (!string.IsNullOrEmpty(sources))
        {
            yield return sources;
        }
    }

    /// <summary>
    /// Stream LLM response token by token using configured settings.
    /// </summary>
    private async IAsyncEnumerable<string> StreamLLMAsync(
        string prompt,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var settings = new OpenAIPromptExecutionSettings
        {
            MaxTokens = _llmSettings.MaxTokens,
            Temperature = _llmSettings.Temperature,
            TopP = _llmSettings.TopP
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

    private string FormatContextOnly(RetrievalResult retrievalResult)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(retrievalResult.Context);
        sb.Append(FormatSources(retrievalResult.SelectedChunks));
        return sb.ToString();
    }

    /// <summary>
    /// Format sources for display.
    /// </summary>
    private static string FormatSources(IReadOnlyList<ScoredChunk> chunks)
    {
        if (chunks.Count == 0)
            return string.Empty;

        var sourceLines = chunks
            .Select((chunk, index) =>
            {
                var parts = new List<string> { $"[{index + 1}] {chunk.DocName ?? "Unknown"}" };
                if (chunk.Page.HasValue)
                    parts.Add($"Page {chunk.Page}");
                if (!string.IsNullOrEmpty(chunk.SectionTitle))
                    parts.Add(chunk.SectionTitle);
                return string.Join(" - ", parts);
            });

        return "\n\n---\n📚 **Sources:**\n" + string.Join("\n", sourceLines);
    }

    /// <summary>
    /// Resolve collection name from tenant ID.
    /// Falls back to default collection if tenant is empty.
    /// </summary>
    private string ResolveCollectionName(string tenantId)
    {
        return string.IsNullOrWhiteSpace(tenantId)
            ? _qdrantSettings.DefaultCollection
            : tenantId;
    }
}
