using Qdrant.Client;
using Microsoft.SemanticKernel.Embeddings;
using Qdrant.Client.Grpc;
using Google.Protobuf.Collections;
using System.Text;

namespace SK_UserGuide.Services.Concrete;

public class RagIngestionService
{
    private readonly QdrantClient _qdrantClient;
    private readonly ITextEmbeddingGenerationService _embeddingService;
    private readonly string _collectionName;

    public RagIngestionService(QdrantClient qdrantClient, ITextEmbeddingGenerationService embeddingService, IConfiguration configuration)
    {
        _qdrantClient = qdrantClient;
        _embeddingService = embeddingService;
        _collectionName = configuration["Qdrant:Collection"];
    }

    public async Task IngestDocumentAsync(string docId, string text, Dictionary<string, object> metadata)
    {
        // Ensure collection exists
        var collections = await _qdrantClient.ListCollectionsAsync();
        if (!collections.Any(c => c == _collectionName))
        {
            await _qdrantClient.CreateCollectionAsync(_collectionName, new VectorParams { Size = 768, Distance = Distance.Cosine });
        }

        var chunks = ChunkText(text);
        var texts = chunks.Select(c => c.Text).ToList();
        var vectors = await _embeddingService.GenerateEmbeddingsAsync(texts);

        var points = new List<PointStruct>();
        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var vector = vectors[i];
            var id = $"{docId}:{metadata.GetValueOrDefault("version", "1")}:{i}";
            var point = new PointStruct
            {
                Id = new PointId { Uuid = id },
                Vectors = vector.ToArray()
            };
            point.Payload.Add("doc_id", new Value { StringValue = docId });
            point.Payload.Add("title", new Value { StringValue = metadata.GetValueOrDefault("title", "").ToString() });
            point.Payload.Add("section", new Value { StringValue = chunk.Section });
            point.Payload.Add("path", new Value { StringValue = metadata.GetValueOrDefault("path", "").ToString() });
            point.Payload.Add("version", new Value { StringValue = metadata.GetValueOrDefault("version", "1").ToString() });
            point.Payload.Add("updated_at", new Value { StringValue = DateTime.UtcNow.ToString() });
            point.Payload.Add("chunk_index", new Value { IntegerValue = i });
            point.Payload.Add("text", new Value { StringValue = chunk.Text });
            point.Payload.Add("source_type", new Value { StringValue = metadata.GetValueOrDefault("source_type", "text").ToString() });

            points.Add(point);
        }

        await _qdrantClient.UpsertAsync(_collectionName, points);
    }

    private List<Chunk> ChunkText(string text)
    {
        var chunks = new List<Chunk>();
        var lines = text.Split('\n');
        var currentChunk = new StringBuilder();
        string currentSection = "";

        foreach (var line in lines)
        {
            if (line.StartsWith("#") || line.Length < 50) // Simple header detection
            {
                if (currentChunk.Length > 0)
                {
                    chunks.Add(new Chunk { Text = currentChunk.ToString(), Section = currentSection });
                    currentChunk.Clear();
                }
                currentSection = line;
                currentChunk.AppendLine(line);
            }
            else
            {
                currentChunk.AppendLine(line);
                if (currentChunk.Length > 4000) // Approx 800 tokens
                {
                    chunks.Add(new Chunk { Text = currentChunk.ToString(), Section = currentSection });
                    currentChunk.Clear();
                }
            }
        }
        if (currentChunk.Length > 0)
        {
            chunks.Add(new Chunk { Text = currentChunk.ToString(), Section = currentSection });
        }
        return chunks;
    }

    private class Chunk
    {
        public string Text { get; set; }
        public string Section { get; set; }
    }
}