using Microsoft.SemanticKernel;
using SK_UserGuide.Services.LLM;
using SK_UserGuide.Services.Retrieval;

namespace SK_UserGuide.Services.Concrete;

/// <summary>
/// RAG retrieval service with context compression and structured responses.
/// </summary>
public class RagRetrievalService
{
    private readonly HybridRetrievalService _hybridRetrieval;
    private readonly Kernel _kernel;
    private readonly ContextCompressor _compressor;
    private readonly PromptBuilder _promptBuilder;
    private readonly ResponseFormatter _responseFormatter;

    public RagRetrievalService(
        HybridRetrievalService hybridRetrieval,
        Kernel kernel)
    {
        _hybridRetrieval = hybridRetrieval;
        _kernel = kernel;
        _compressor = new ContextCompressor();
        _promptBuilder = new PromptBuilder();
        _responseFormatter = new ResponseFormatter();
    }

    /// <summary>
    /// Process a question with RAG pipeline.
    /// </summary>
    public async Task<string> AskAsync(string question)
    {
        // 1. Detect question language
        var questionLanguage = LanguageDetector.Detect(question);

        // 2. Hybrid retrieval
        var retrievalResult = await _hybridRetrieval.RetrieveAsync(question);

        if (retrievalResult.ChunkCount == 0)
        {
            var noResult = _responseFormatter.CreateNoResultsResponse(questionLanguage);
            return noResult.ToDisplayString();
        }

        // 3. Context compression (extractive, no LLM)
        var compressedContext = _compressor.Compress(
            retrievalResult.SelectedChunks,
            question,
            questionLanguage);

        // 4. Build prompt
        var prompt = _promptBuilder.Build(question, compressedContext);

        // 5. Call LLM
        var rawAnswer = await CallLLMAsync(prompt);

        // 6. Format response
        var response = _responseFormatter.Format(
            rawAnswer,
            compressedContext,
            questionLanguage);

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
                    ["max_tokens"] = 256,      // ~150-200 words max
                    ["temperature"] = 0.3      // Lower = more focused answers
                }
            };

            var result = await _kernel.InvokePromptAsync(prompt, new KernelArguments(settings), cancellationToken: cts.Token);
            return result.GetValue<string>() ?? "Cevap oluşturulamadı.";
        }
        catch (OperationCanceledException)
        {
            return "LLM yanıt süresi doldu. Lütfen daha kısa bir soru sorun veya daha sonra tekrar deneyin.";
        }
    }
}
