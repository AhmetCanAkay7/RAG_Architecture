using Microsoft.SemanticKernel;
using SK_UserGuide.Services.Abstract;
using SK_UserGuide.Services.LLM;
using SK_UserGuide.Services.Retrieval;

namespace SK_UserGuide.Services.Concrete;

/// <summary>
/// RAG retrieval service with context compression and structured responses.
/// </summary>
public class RagRetrievalService : IRagRetrievalService
{
    private readonly IHybridRetrievalService _hybridRetrieval;
    private readonly Kernel _kernel;
    private readonly ContextCompressor _compressor;
    private readonly PromptBuilder _promptBuilder;
    private readonly ResponseFormatter _responseFormatter;
    private readonly QueryTranslator _queryTranslator;

    public RagRetrievalService(
        IHybridRetrievalService hybridRetrieval,
        Kernel kernel,
        QueryTranslator? queryTranslator = null)
    {
        _hybridRetrieval = hybridRetrieval;
        _kernel = kernel;
        _compressor = new ContextCompressor();
        _promptBuilder = new PromptBuilder();
        _responseFormatter = new ResponseFormatter();
        _queryTranslator = queryTranslator ?? new QueryTranslator(kernel, enabled: false);
    }

    /// <summary>
    /// Process a question with RAG pipeline.
    /// </summary>
    public async Task<string> AskAsync(string question)
    {
        // 1. Optionally translate query to English
        var translationResult = await _queryTranslator.TranslateIfNeededAsync(question);
        var processedQuestion = translationResult.Query;

        // 2. Hybrid retrieval
        var retrievalResult = await _hybridRetrieval.RetrieveAsync(processedQuestion);

        if (retrievalResult.ChunkCount == 0)
        {
            var noResult = _responseFormatter.CreateNoResultsResponse("EN");
            return noResult.ToDisplayString();
        }

        // 3. Context compression (extractive, no LLM)
        var compressedContext = _compressor.Compress(
            retrievalResult.SelectedChunks,
            processedQuestion,
            "EN");

        // 4. Build prompt
        var prompt = _promptBuilder.Build(
            processedQuestion,
            compressedContext);

        // 5. Call LLM
        var rawAnswer = await CallLLMAsync(prompt);

        // 6. Format response
        var response = _responseFormatter.Format(
            rawAnswer,
            compressedContext,
            "EN");

        return response.ToDisplayString();
    }

    /// <summary>
    /// Call LLM with the built prompt.
    /// </summary>
    private async Task<string> CallLLMAsync(string prompt)
    {
        // 5 minute timeout - Ollama may need time to load model into memory
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        try
        {
            // Limit response length for faster generation on CPU
            var settings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    ["max_tokens"] = 512,
                    ["temperature"] = 0.2,
                    ["top_p"] = 0.85,
                    ["top_k"] = 50,
                    ["num_thread"] = 8,          // CPU thread count for parallel inference
                    ["stop"] = new[] { "\n\n", "QUESTION:", "CONTEXT:" }  // Stop sequences
                }
            };

            var result = await _kernel.InvokePromptAsync(prompt, new KernelArguments(settings), cancellationToken: cts.Token);
            return result.GetValue<string>() ?? "Unable to generate response.";
        }
        catch (OperationCanceledException)
        {
            return "LLM response timed out. Please try a shorter question or try again later.";
        }
    }
}
