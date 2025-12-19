using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using SK_UserGuide.Configuration;
using System.Text;

namespace SK_UserGuide.Services.Concrete;

/// <summary>
/// Service for ingesting documents into Qdrant vector store.
/// </summary>
public class RagIngestionService
{
    private readonly QdrantClient _qdrantClient;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly QdrantSettings _settings;

    public RagIngestionService(
        QdrantClient qdrantClient,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IOptions<QdrantSettings> options)
    {
        _qdrantClient = qdrantClient;
        _embeddingGenerator = embeddingGenerator;
        _settings = options.Value;
    }

    public async Task IngestDocumentAsync(string docId, string text, Dictionary<string, object> metadata)
    {
        // Ensure collection exists
        var collections = await _qdrantClient.ListCollectionsAsync();
        if (!collections.Any(c => c == _settings.Collection))
        {
            await _qdrantClient.CreateCollectionAsync(_settings.Collection,
                new VectorParams { Size = 768, Distance = Distance.Cosine });
        }

        var chunks = ChunkText(text);
        var texts = chunks.Select(c => c.Text).ToList();

        // Generate embeddings
        var embeddings = await _embeddingGenerator.GenerateAsync(texts);

        var points = new List<PointStruct>();
        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            // Generate a unique numeric ID from docId and chunk index
            var idString = $"{docId}:{i}";
            var numericId = (ulong)Math.Abs(idString.GetHashCode()) + (ulong)i;
            var point = new PointStruct
            {
                Id = new PointId { Num = numericId },
                Vectors = embeddings[i].Vector.ToArray()
            };

            point.Payload.Add("doc_id", new Value { StringValue = docId });
            point.Payload.Add("title", new Value { StringValue = metadata.GetValueOrDefault("title", "")?.ToString() ?? "" });
            point.Payload.Add("section", new Value { StringValue = chunk.Section });
            point.Payload.Add("path", new Value { StringValue = metadata.GetValueOrDefault("path", "")?.ToString() ?? "" });
            point.Payload.Add("version", new Value { StringValue = metadata.GetValueOrDefault("version", "1")?.ToString() ?? "1" });
            point.Payload.Add("updated_at", new Value { StringValue = DateTime.UtcNow.ToString("o") });
            point.Payload.Add("chunk_index", new Value { IntegerValue = i });
            point.Payload.Add("text", new Value { StringValue = chunk.Text });
            point.Payload.Add("source_type", new Value { StringValue = metadata.GetValueOrDefault("source_type", "text")?.ToString() ?? "text" });

            points.Add(point);
        }

        await _qdrantClient.UpsertAsync(_settings.Collection, points);
    }

    private List<TextChunk> ChunkText(string text, int maxChunkSize = 4000)
    {
        var chunks = new List<TextChunk>();
        var lines = text.Split('\n');
        var currentChunk = new StringBuilder();
        var currentSection = string.Empty;

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith('#') || (line.Length > 0 && line.Length < 50 && !line.Contains('.')))
            {
                if (currentChunk.Length > 0)
                {
                    chunks.Add(new TextChunk(currentChunk.ToString().Trim(), currentSection));
                    currentChunk.Clear();
                }
                currentSection = line.Trim();
                currentChunk.AppendLine(line);
            }
            else
            {
                currentChunk.AppendLine(line);

                if (currentChunk.Length > maxChunkSize)
                {
                    chunks.Add(new TextChunk(currentChunk.ToString().Trim(), currentSection));
                    currentChunk.Clear();
                }
            }
        }

        if (currentChunk.Length > 0)
        {
            chunks.Add(new TextChunk(currentChunk.ToString().Trim(), currentSection));
        }

        return chunks;
    }

    private record TextChunk(string Text, string Section);
}