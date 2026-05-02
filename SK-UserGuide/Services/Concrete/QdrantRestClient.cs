using System.Net.Http.Json;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SK_UserGuide.Configuration;

namespace SK_UserGuide.Services.Concrete;

/// <summary>
/// REST-based Qdrant client that uses HTTP/1.1 to bypass corporate proxy HTTP/2 blocking.
/// All operations accept collectionName parameter for multi-tenant support.
/// </summary>
public class QdrantRestClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly QdrantSettings _settings;
    private readonly LlmSettings _llmSettings;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public QdrantRestClient(
        HttpClient httpClient,
        IOptions<QdrantSettings> qdrantOptions,
        IOptions<LlmSettings> llmOptions)
    {
        _httpClient = httpClient;
        _settings = qdrantOptions.Value;
        _llmSettings = llmOptions.Value;
        _httpClient.BaseAddress = new Uri(_settings.Host);
    }

    /// <summary>
    /// Lists all collections in Qdrant.
    /// </summary>
    public async Task<List<string>> ListCollectionsAsync()
    {
        var response = await _httpClient.GetAsync("/collections");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CollectionsResponse>(_jsonOptions);
        return result?.Result?.Collections?.Select(c => c.Name).ToList() ?? new List<string>();
    }

    /// <summary>
    /// Checks whether a tenant collection exists.
    /// </summary>
    public async Task<bool> CollectionExistsAsync(string collectionName)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            return false;

        var response = await _httpClient.GetAsync($"/collections/{collectionName}");
        if (response.StatusCode == HttpStatusCode.NotFound)
            return false;

        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>
    /// Creates a collection if it doesn't exist.
    /// Vector size is determined by LlmSettings.EmbeddingDimensions.
    /// </summary>
    public async Task CreateCollectionIfNotExistsAsync(string collectionName)
    {
        var collections = await ListCollectionsAsync();
        if (collections.Contains(collectionName))
            return;

        var payload = new
        {
            vectors = new
            {
                size = _llmSettings.EmbeddingDimensions,
                distance = "Cosine"
            }
        };

        var response = await _httpClient.PutAsJsonAsync($"/collections/{collectionName}", payload, _jsonOptions);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Upserts points (vectors with payload) into a collection.
    /// </summary>
    public async Task UpsertAsync(string collectionName, List<PointStruct> points)
    {
        // Convert to proper format for Qdrant API
        var pointsForApi = points.Select(p => new
        {
            id = p.Id,
            vector = p.Vector,
            payload = p.Payload
        }).ToList();

        var payload = new { points = pointsForApi };

        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PutAsync(
            $"/collections/{collectionName}/points?wait=true",
            content);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Qdrant upsert failed: {response.StatusCode} - {errorBody}");
        }
    }

    /// <summary>
    /// Searches for similar vectors in a collection.
    /// </summary>
    public async Task<List<SearchResult>> SearchAsync(string collectionName, float[] vector, int limit = 5)
    {
        var payload = new
        {
            vector,
            limit,
            with_payload = true,
            score_threshold = 0.53f  // Minimum cosine similarity threshold
        };

        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(
            $"/collections/{collectionName}/points/search",
            content);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new QdrantCollectionNotFoundException(collectionName);

            throw new HttpRequestException($"Qdrant search failed: {response.StatusCode} - {errorBody}");
        }

        var result = await response.Content.ReadFromJsonAsync<SearchResponse>(_jsonOptions);
        return result?.Result ?? new List<SearchResult>();
    }

    /// <summary>
    /// Deletes a collection.
    /// </summary>
    public async Task DeleteCollectionAsync(string collectionName)
    {
        var response = await _httpClient.DeleteAsync($"/collections/{collectionName}");
        response.EnsureSuccessStatusCode();
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    // DTOs for Qdrant REST API
    public class CollectionsResponse
    {
        [JsonPropertyName("result")]
        public CollectionsResult? Result { get; set; }
    }

    public class CollectionsResult
    {
        [JsonPropertyName("collections")]
        public List<CollectionInfo>? Collections { get; set; }
    }

    public class CollectionInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    public class PointStruct
    {
        public ulong Id { get; set; }
        public float[] Vector { get; set; } = Array.Empty<float>();
        public Dictionary<string, object> Payload { get; set; } = new();
    }

    public class SearchResponse
    {
        [JsonPropertyName("result")]
        public List<SearchResult>? Result { get; set; }
    }

    public class SearchResult
    {
        [JsonPropertyName("id")]
        public ulong Id { get; set; }

        [JsonPropertyName("score")]
        public float Score { get; set; }

        [JsonPropertyName("payload")]
        public Dictionary<string, JsonElement>? Payload { get; set; }
    }
}
