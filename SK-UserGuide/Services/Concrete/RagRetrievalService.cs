using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using OpenAI.Assistants;
using SK_UserGuide.Configuration;
using SK_UserGuide.Services.Abstract;
using SK_UserGuide.Services.LLM;
using SK_UserGuide.Services.Retrieval;

namespace SK_UserGuide.Services.Concrete;

/// <summary>
/// RAG retrieval service with configurable response modes.
/// </summary>
public class RagRetrievalService : IRagRetrievalService
{
    private readonly IHybridRetrievalService _hybridRetrieval;
    private readonly Kernel _kernel;
    private readonly ContextCompressor _compressor;
    private readonly PromptBuilder _promptBuilder;
    private readonly ResponseFormatter _responseFormatter;
    private readonly QueryTranslator _queryTranslator;
    private readonly string _responseMode;

    // System prompts
    private const string ChatSystemPrompt = "You are a helpful assistant.Respond briefly in English.";
    
    public RagRetrievalService(
        IHybridRetrievalService hybridRetrieval,
        Kernel kernel,
        IOptions<RagSettings> ragSettings,
        QueryTranslator? queryTranslator = null)
    {
        _hybridRetrieval = hybridRetrieval;
        _kernel = kernel;
        _compressor = new ContextCompressor();
        _promptBuilder = new PromptBuilder();
        _responseFormatter = new ResponseFormatter();
        _queryTranslator = queryTranslator ?? new QueryTranslator(kernel, enabled: false);
        _responseMode = ragSettings.Value.ResponseMode;
    }

    /// <summary>
    /// Process a question with configurable response mode.
    /// </summary>
    public async Task<string> AskAsync(string question)
    {
        // 1. Optionally translate query to English
        string processedQuestion = await TranslateAsync(question);

        // 2. Try to get context
        var retrievalResult = await _hybridRetrieval.RetrieveAsync(processedQuestion);

        // 3. No context found → Chat mode (LLM sohbet)
        if (retrievalResult.ChunkCount == 0)
        {
            return await HandleChatAsync(processedQuestion);
        }

        // 4. Context found → Check response mode
        if (_responseMode == "ContextOnly")
        {
            return HandleContextOnly(processedQuestion,retrievalResult);
        }
        else // Hybrid
        {
            return await HandleHybridAsync(processedQuestion, retrievalResult);
        }
    }

    /// <summary>
    /// Chat mode: Direct LLM response without context (for greetings, general conversation).
    /// </summary>
    private async Task<string> HandleChatAsync(string question)
    {
        var prompt = $"{ChatSystemPrompt}\n\nUser: {question}\nAssistant:";
        return await CallLLMAsync(prompt);
    }

    /// <summary>
    /// ContextOnly mode: Return compressed context directly (fastest).
    /// </summary>
    private string HandleContextOnly(string question, RetrievalResult retrievalResult)
    {
        var compressedContext = _compressor.Compress(
            retrievalResult.SelectedChunks,
            question,
            "EN");

        var response = _responseFormatter.Format(
            compressedContext.Summary,
            compressedContext,
            "EN");

        return response.ToDisplayString();
    }

    /// <summary>
    /// Hybrid mode: Context + LLM processing (current behavior).
    /// </summary>
    private async Task<string> HandleHybridAsync(string question, RetrievalResult retrievalResult)
    {
        // Context compression
        var compressedContext = _compressor.Compress(
            retrievalResult.SelectedChunks,
            question,
            "EN");

        // Build prompt with RAG system prompt
        var prompt = _promptBuilder.Build(question, compressedContext);

        // Call LLM
        var rawAnswer = await CallLLMAsync(prompt);

        // Format response
        var response = _responseFormatter.Format(
            rawAnswer,
            compressedContext,
            "EN");

        return response.ToDisplayString();
    }

    /// <summary>
    /// Call LLM with the given prompt.
    /// </summary>
    private async Task<string> CallLLMAsync(string prompt)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        try
        {
            var settings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    ["max_tokens"] = 512,
                    ["temperature"] = 0.2,
                    ["top_p"] = 0.85,
                    ["num_thread"] = Environment.ProcessorCount
                }
            };

            var result = await _kernel.InvokePromptAsync(prompt, new KernelArguments(settings), cancellationToken: cts.Token);
            return result.GetValue<string>() ?? "Unable to generate response.";
        }
        catch (OperationCanceledException)
        {
            return "LLM response timed out. Please try again.";
        }
    }

    private async Task<string> TranslateAsync(string question)
    {
        var translationResult = await _queryTranslator.TranslateIfNeededAsync(question);
        return translationResult.Query;
    }
}
