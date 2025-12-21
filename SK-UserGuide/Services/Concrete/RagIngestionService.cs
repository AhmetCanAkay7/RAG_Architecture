using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using SK_UserGuide.Configuration;
using System.Text;

namespace SK_UserGuide.Services.Concrete;

/// <summary>
/// Service for ingesting documents into Qdrant vector store using REST API.
/// </summary>
public class RagIngestionService
{
    private readonly QdrantRestClient _qdrantClient;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly QdrantSettings _settings;

    public RagIngestionService(
        QdrantRestClient qdrantClient,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IOptions<QdrantSettings> options)
    {
        _qdrantClient = qdrantClient;
        _embeddingGenerator = embeddingGenerator;
        _settings = options.Value;
    }

    public async Task IngestDocumentAsync(string docId, string text, Dictionary<string, object> metadata)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Document text cannot be empty", nameof(text));
        }

        // Ensure collection exists
        await _qdrantClient.CreateCollectionIfNotExistsAsync(_settings.Collection, 768);

        var chunks = ChunkText(text);

        // Filter out empty chunks
        chunks = chunks.Where(c => !string.IsNullOrWhiteSpace(c.Text)).ToList();

        if (chunks.Count == 0)
        {
            throw new InvalidOperationException("No valid text chunks could be created from the document");
        }

        var texts = chunks.Select(c => c.Text).ToList();

        // Generate embeddings
        var embeddings = await _embeddingGenerator.GenerateAsync(texts);

        var points = new List<QdrantRestClient.PointStruct>();
        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            // Generate a unique numeric ID from docId and chunk index
            var idString = $"{docId}:{i}";
            var numericId = (ulong)Math.Abs(idString.GetHashCode()) + (ulong)i;

            // Properly extract the vector from ReadOnlyMemory<float>
            var vectorMemory = embeddings[i].Vector;
            var vector = vectorMemory.ToArray();

            if (vector.Length == 0)
            {
                throw new InvalidOperationException($"Embedding for chunk {i} is empty. Ollama may have failed to generate embeddings.");
            }

            var point = new QdrantRestClient.PointStruct
            {
                Id = numericId,
                Vector = vector,
                Payload = new Dictionary<string, object>
                {
                    ["doc_id"] = docId,
                    ["title"] = metadata.GetValueOrDefault("title", "")?.ToString() ?? "",
                    ["section"] = chunk.Section,
                    ["path"] = metadata.GetValueOrDefault("path", "")?.ToString() ?? "",
                    ["version"] = metadata.GetValueOrDefault("version", "1")?.ToString() ?? "1",
                    ["updated_at"] = DateTime.UtcNow.ToString("o"),
                    ["chunk_index"] = i,
                    ["text"] = chunk.Text,
                    ["source_type"] = metadata.GetValueOrDefault("source_type", "text")?.ToString() ?? "text"
                }
            };

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