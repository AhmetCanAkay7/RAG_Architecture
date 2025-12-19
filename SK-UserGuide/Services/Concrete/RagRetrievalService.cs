using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Qdrant.Client;
using SK_UserGuide.Configuration;
using System.Text;

namespace SK_UserGuide.Services.Concrete;

/// <summary>
/// Service for retrieving documents and generating answers using RAG.
/// </summary>
public class RagRetrievalService
{
    private readonly QdrantClient _qdrantClient;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly Kernel _kernel;
    private readonly QdrantSettings _settings;
    private const double MinScoreThreshold = 0.5;

    public RagRetrievalService(
        QdrantClient qdrantClient,
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

        foreach (var point in searchResult)
        {
            if (point.Score < MinScoreThreshold) continue;

            context.AppendLine($"- {point.Payload["text"].StringValue}");
            sources.Add($"Bolum: {point.Payload["section"].StringValue}, Chunk: {point.Payload["chunk_index"].IntegerValue}");
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

        var promptResult = await _kernel.InvokePromptAsync(prompt);
        var answer = promptResult.GetValue<string>() ?? "Cevap olusturulamadi.";

        return $"{answer}\n\nKaynaklar:\n{string.Join("\n", sources)}";
    }
}
