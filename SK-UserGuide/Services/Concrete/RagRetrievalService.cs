using Qdrant.Client;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel;
using Qdrant.Client.Grpc;
using System.Text;

namespace SK_UserGuide.Services.Concrete;

public class RagRetrievalService
{
    private readonly QdrantClient _qdrantClient;
    private readonly ITextEmbeddingGenerationService _embeddingService;
    private readonly Kernel _kernel;
    private readonly string _collectionName;

    public RagRetrievalService(QdrantClient qdrantClient, ITextEmbeddingGenerationService embeddingService, Kernel kernel, IConfiguration configuration)
    {
        _qdrantClient = qdrantClient;
        _embeddingService = embeddingService;
        _kernel = kernel;
        _collectionName = configuration["Qdrant:Collection"];
    }

    public async Task<string> AskAsync(string question)
    {
        var queryVector = (await _embeddingService.GenerateEmbeddingsAsync(new List<string> { question }))[0];
        var searchResult = await _qdrantClient.SearchAsync(_collectionName, queryVector.ToArray(), limit: 5);

        var context = new StringBuilder();
        var sources = new List<string>();
        double minScore = 0.7; // Threshold for answerability

        foreach (var point in searchResult)
        {
            if (point.Score < minScore) continue;
            context.AppendLine($"- {point.Payload["text"].StringValue}");
            sources.Add($"Doc: {point.Payload["doc_id"].StringValue}, Section: {point.Payload["section"].StringValue}, Path: {point.Payload["path"].StringValue}, Chunk: {point.Payload["chunk_index"].IntegerValue}");
        }

        if (context.Length == 0)
        {
            return "Bu bilgi KB'de do?rulanamad?. Önerilen aramalar: [?irket politikalar?, kullan?m k?lavuzu, IT destek]. Eksik dokümanlar: [güncel prosedürler].";
        }

        var prompt = $@"
Sen yard?mc? bir asistans?n. A?a??daki [BA?LAM] bilgisini kullanarak cevap ver.

[BA?LAM]:
{context}

[SORU]: {question}

[CEVAP]:";
        
        var result = await _kernel.InvokePromptAsync(prompt);
        var answer = result.GetValue<string>();

        return $"{answer}\n\nKaynaklar:\n{string.Join("\n", sources)}";
    }
}