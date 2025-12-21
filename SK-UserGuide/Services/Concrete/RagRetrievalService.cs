using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using SK_UserGuide.Configuration;
using System.Text;

namespace SK_UserGuide.Services.Concrete;

/// <summary>
/// Service for retrieving documents and generating answers using RAG with REST API.
/// </summary>
public class RagRetrievalService
{
    private readonly QdrantRestClient _qdrantClient;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly Kernel _kernel;
    private readonly QdrantSettings _settings;
    private const double MinScoreThreshold = 0.5;

    public RagRetrievalService(
        QdrantRestClient qdrantClient,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        Kernel kernel,
        IOptions<QdrantSettings> options)
    {
        _qdrantClient = qdrantClient;
        _embeddingGenerator = embeddingGenerator;
        _kernel = kernel;
        _settings = options.Value;
    }

    public async Task<string> AskAsync(string question)
    {
        // Generate embedding for the question
        var questionEmbeddings = await _embeddingGenerator.GenerateAsync([question]);
        var queryVector = questionEmbeddings.First().Vector.ToArray();

        // Search for similar documents
        var searchResult = await _qdrantClient.SearchAsync(_settings.Collection, queryVector, limit: 5);

        var context = new StringBuilder();
        var sources = new List<string>();
        const int MaxContextLength = 8000; // Limit context to prevent LLM timeout

        foreach (var point in searchResult)
        {
            if (point.Score < MinScoreThreshold) continue;

            // Extract text from payload
            var text = point.Payload?.TryGetValue("text", out var textElement) == true
                ? textElement.GetString() ?? ""
                : "";
            var section = point.Payload?.TryGetValue("section", out var sectionElement) == true
                ? sectionElement.GetString() ?? ""
                : "";
            var chunkIndex = point.Payload?.TryGetValue("chunk_index", out var chunkElement) == true
                ? chunkElement.GetInt32()
                : 0;

            // Check if adding this chunk would exceed limit
            if (context.Length + text.Length > MaxContextLength)
            {
                break; // Stop adding more context
            }

            context.AppendLine($"- {text}");
            sources.Add($"Bolum: {section}, Chunk: {chunkIndex}");
        }

        if (context.Length == 0)
        {
            return "Bu bilgi veritabaninda bulunamadi. Lutfen sorunuzu farkli sekilde sormayi deneyin veya yoneticinize basvurun.";
        }

        var prompt = $@"Sen yardimci bir asistansin. Asagidaki [BAGLAM] bilgisini kullanarak kullanicinin sorusuna Turkce olarak cevap ver.
Eger baglamda yeterli bilgi yoksa, bunu belirt.

[BAGLAM]:
{context}

[SORU]: {question}

[CEVAP]:";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120)); // 2 minute timeout
        try
        {
            var promptResult = await _kernel.InvokePromptAsync(prompt, cancellationToken: cts.Token);
            var answer = promptResult.GetValue<string>() ?? "Cevap olusturulamadi.";
            return $"{answer}\n\nKaynaklar:\n{string.Join("\n", sources)}";
        }
        catch (OperationCanceledException)
        {
            return "LLM yanit suresi doldu. Lutfen daha kisa bir soru sorun veya daha sonra tekrar deneyin.";
        }
    }
}
