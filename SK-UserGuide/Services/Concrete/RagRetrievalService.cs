using Microsoft.SemanticKernel;
using SK_UserGuide.Services.LLM;
using SK_UserGuide.Services.Retrieval;

namespace SK_UserGuide.Services.Concrete;

/// <summary>
/// Enhanced RAG retrieval service with multilingual support,
/// context compression, and structured responses.
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
    /// Process a question with enhanced multilingual RAG pipeline.
    /// </summary>
    public async Task<string> AskAsync(string question)
    {
        // 1. Detect question language
        var questionLanguage = LanguageDetector.Detect(question);

        // 2. Detect question type for formatting
        var questionType = QuestionTypeDetector.Detect(question);

        // 3. Hybrid retrieval
        var retrievalResult = await _hybridRetrieval.RetrieveAsync(question);

        if (retrievalResult.ChunkCount == 0)
        {
            var noResult = _responseFormatter.CreateNoResultsResponse(questionLanguage);
            return noResult.ToDisplayString();
        }

        // 4. Context compression (extractive, no LLM)
        var compressedContext = _compressor.Compress(
            retrievalResult.SelectedChunks,
            question,
            questionLanguage);

        // 5. Optional LLM-based compression (if needed)
        if (_compressor.ShouldUseLLMCompression(question, compressedContext, questionType))
        {
            compressedContext = await LLMCompressAsync(question, compressedContext);
        }

        // 6. Build prompt with enhanced system prompt
        var prompt = _promptBuilder.Build(
            question,
            compressedContext,
            questionLanguage,
            questionType);

        // 7. Call LLM
        var rawAnswer = await CallLLMAsync(prompt);

        // 8. Format response
        var response = _responseFormatter.Format(
            rawAnswer,
            compressedContext,
            questionLanguage);

        return response.ToDisplayString();
    }

    /// <summary>
    /// Optional LLM-based context compression.
    /// Used only when extractive compression is not sufficient.
    /// </summary>
    private async Task<CompressedContext> LLMCompressAsync(
        string question,
        CompressedContext context)
    {
        try
        {
            var compressionPrompt = _promptBuilder.BuildCompressionPrompt(question, context);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await _kernel.InvokePromptAsync(compressionPrompt, cancellationToken: cts.Token);
            var compressedText = result.GetValue<string>() ?? "";

            if (!string.IsNullOrWhiteSpace(compressedText))
            {
                // Create a single compressed context item
                return new CompressedContext
                {
                    Items = new List<ContextItem>
                    {
                        new ContextItem
                        {
                            Index = 1,
                            Text = compressedText,
                            DocName = "Birleştirilmiş Kaynaklar",
                            Language = LanguageDetector.Detect(compressedText),
                            OriginalScore = context.AverageScore
                        }
                    }
                };
            }
        }
        catch
        {
            // Fall back to original context if compression fails
        }

        return context;
    }

    /// <summary>
    /// Call LLM with the built prompt.
    /// </summary>
    private async Task<string> CallLLMAsync(string prompt)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        try
        {
            var result = await _kernel.InvokePromptAsync(prompt, cancellationToken: cts.Token);
            return result.GetValue<string>() ?? "Cevap oluşturulamadı.";
        }
        catch (OperationCanceledException)
        {
            return "LLM yanıt süresi doldu. Lütfen daha kısa bir soru sorun veya daha sonra tekrar deneyin.";
        }
    }
}
