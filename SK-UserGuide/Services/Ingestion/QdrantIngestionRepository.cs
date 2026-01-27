using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SK_UserGuide.Configuration;
using SK_UserGuide.Services.Abstract;

namespace SK_UserGuide.Services.Ingestion;

/// <summary>
/// Repository for ingesting chunks into Qdrant with proper metadata.
/// </summary>
public class QdrantIngestionRepository : IQdrantIngestionRepository, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly QdrantSettings _settings;
    private readonly DocumentMetadataBuilder _metadataBuilder;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public QdrantIngestionRepository(
        HttpClient httpClient,
        IOptions<QdrantSettings> options,
        DocumentMetadataBuilder metadataBuilder)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _metadataBuilder = metadataBuilder;
        _httpClient.BaseAddress = new Uri(_settings.Host);
    }

    /// <summary>
    /// Ensure collection exists with proper configuration.
    /// </summary>
    public async Task EnsureCollectionAsync()
    {
        // Check if collection exists
        var response = await _httpClient.GetAsync($"/collections/{_settings.Collection}");
        if (response.IsSuccessStatusCode)
            return;

        // Create collection
        var createRequest = new
        {
            vectors = new
            {
                size = 768, // nomic-embed-text dimension
                distance = "Cosine"
            },
            hnsw_config = new
            {
                m = 16,
                ef_construct = 100
            }
        };

        var createResponse = await _httpClient.PutAsJsonAsync(
            $"/collections/{_settings.Collection}",
            createRequest,
            _jsonOptions);

        createResponse.EnsureSuccessStatusCode();

        // Create payload indexes for filtering
        await CreatePayloadIndexAsync("doc_id", "keyword");
        await CreatePayloadIndexAsync("section_title", "keyword");

        // Create full-text index for sparse search
        await CreateFullTextIndexAsync("text");
    }

    /// <summary>
    /// Create a full-text index for text search.
    /// </summary>
    private async Task CreateFullTextIndexAsync(string fieldName)
    {
        try
        {
            var indexRequest = new
            {
                field_name = fieldName,
                field_schema = new
                {
                    type = "text",
                    tokenizer = "word",
                    min_token_len = 2,
                    max_token_len = 40,
                    lowercase = true
                }
            };

            await _httpClient.PutAsJsonAsync(
                $"/collections/{_settings.Collection}/index",
                indexRequest,
                _jsonOptions);
        }
        catch
        {
            // Index creation may fail if already exists - that's ok
        }
    }

    /// <summary>
    /// Create a payload field index.
    /// </summary>
    private async Task CreatePayloadIndexAsync(string fieldName, string fieldType)
    {
        try
        {
            var indexRequest = new
            {
                field_name = fieldName,
                field_schema = fieldType
            };

            await _httpClient.PutAsJsonAsync(
                $"/collections/{_settings.Collection}/index",
                indexRequest,
                _jsonOptions);
        }
        catch
        {
            // Index creation may fail if already exists - that's ok
        }
    }

    /// <summary>
    /// Upsert chunks with their embeddings and metadata.
    /// </summary>
    public async Task UpsertChunksAsync(
        string docId,
        string docName,
        string sourceType,
        List<ChunkResult> chunks,
        List<float[]> embeddings)
    {
        await EnsureCollectionAsync();

        var points = new List<object>();

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var payload = _metadataBuilder.BuildPayload(docId, docName, sourceType, chunk);
            var pointId = _metadataBuilder.GeneratePointId(docId, chunk.Index);

            points.Add(new
            {
                id = pointId,
                vector = embeddings[i],
                payload = new
                {
                    doc_id = payload.DocId,
                    doc_name = payload.DocName,
                    source_type = payload.SourceType,
                    created_at = payload.CreatedAt.ToString("o"),
                    chunk_index = payload.ChunkIndex,
                    text = payload.Text,
                    language = payload.Language,
                    content_hash = payload.ContentHash,
                    page = payload.Page,
                    section_title = payload.SectionTitle,
                    estimated_tokens = payload.EstimatedTokens,
                    has_overlap = payload.HasOverlap
                }
            });
        }

        // Batch upsert - 50 points at a time
        const int batchSize = 50;
        foreach (var batch in points.Chunk(batchSize))
        {
            var upsertRequest = new { points = batch };
            var json = JsonSerializer.Serialize(upsertRequest, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PutAsync(
                $"/collections/{_settings.Collection}/points?wait=true",
                content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Qdrant upsert failed: {response.StatusCode} - {error}");
            }
        }
    }

    /// <summary>
    /// Delete all points for a document by doc_id.
    /// </summary>
    public async Task<int> DeleteByDocIdAsync(string docId)
    {
        var deleteRequest = new
        {
            filter = new
            {
                must = new[]
                {
                    new { key = "doc_id", match = new { value = docId } }
                }
            }
        };

        return await ExecuteDeleteAsync(deleteRequest);
    }

    /// <summary>
    /// Delete points for a document by doc_name (filename).
    /// </summary>
    public async Task<int> DeleteByDocNameAsync(string docName)
    {
        var deleteRequest = new
        {
            filter = new
            {
                must = new[]
                {
                    new { key = "doc_name", match = new { value = docName } }
                }
            }
        };

        return await ExecuteDeleteAsync(deleteRequest);
    }

    /// <summary>
    /// Get list of all documents in the collection.
    /// </summary>
    public async Task<List<DocumentInfo>> GetAllDocumentsAsync()
    {
        var documents = new Dictionary<string, DocumentInfo>();

        try
        {
            // Scroll through all points to get unique documents
            var scrollRequest = new
            {
                limit = 100,
                with_payload = new { include = new[] { "doc_id", "doc_name", "created_at" } }
            };

            string? nextOffset = null;
            do
            {
                var requestWithOffset = nextOffset != null
                    ? new { scrollRequest.limit, scrollRequest.with_payload, offset = nextOffset }
                    : (object)scrollRequest;

                var response = await _httpClient.PostAsJsonAsync(
                    $"/collections/{_settings.Collection}/points/scroll",
                    requestWithOffset,
                    _jsonOptions);

                if (!response.IsSuccessStatusCode)
                    break;

                var result = await response.Content.ReadFromJsonAsync<ScrollResponse>(_jsonOptions);

                if (result?.Result?.Points == null || result.Result.Points.Count == 0)
                    break;

                foreach (var point in result.Result.Points)
                {
                    var docId = GetPayloadString(point.Payload, "doc_id");
                    var docName = GetPayloadString(point.Payload, "doc_name");

                    if (!string.IsNullOrEmpty(docId))
                    {
                        if (!documents.TryGetValue(docId, out var info))
                        {
                            info = new DocumentInfo
                            {
                                DocId = docId,
                                DocName = docName,
                                ChunkCount = 0
                            };
                            documents[docId] = info;
                        }

                        info.ChunkCount++;
                    }
                }

                nextOffset = result.Result.NextPageOffset;

            } while (nextOffset != null);
        }
        catch
        {
            // Return empty list on error
        }

        return documents.Values.ToList();
    }

    private async Task<int> ExecuteDeleteAsync(object deleteRequest)
    {
        try
        {
            var json = JsonSerializer.Serialize(deleteRequest, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                $"/collections/{_settings.Collection}/points/delete?wait=true",
                content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Qdrant delete failed: {response.StatusCode} - {error}");
            }

            return 1; // Return 1 to indicate success
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch
        {
            return 0;
        }
    }

    private string GetPayloadString(Dictionary<string, JsonElement>? payload, string key)
    {
        if (payload?.TryGetValue(key, out var element) == true)
            return element.ValueKind == JsonValueKind.String ? element.GetString() ?? "" : "";
        return "";
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    // DTOs
    private class ScrollResponse
    {
        [JsonPropertyName("result")]
        public ScrollResult? Result { get; set; }
    }

    private class ScrollResult
    {
        [JsonPropertyName("points")]
        public List<ScrollPoint>? Points { get; set; }

        [JsonPropertyName("next_page_offset")]
        public string? NextPageOffset { get; set; }
    }

    private class ScrollPoint
    {
        [JsonPropertyName("id")]
        public ulong Id { get; set; }

        [JsonPropertyName("payload")]
        public Dictionary<string, JsonElement>? Payload { get; set; }
    }
}

/// <summary>
/// Information about a document in the collection.
/// </summary>
public class DocumentInfo
{
    public required string DocId { get; init; }
    public string DocName { get; set; } = "";
    public int ChunkCount { get; set; }
}
