using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SK_UserGuide.Configuration;
using SK_UserGuide.Services.Abstract;

namespace SK_UserGuide.Services.Ingestion;

/// <summary>
/// Manages document versions in Qdrant.
/// Handles version tracking and old version cleanup.
/// </summary>
public class VersionManager : IVersionManager
{
    private readonly HttpClient _httpClient;
    private readonly QdrantSettings _settings;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public VersionManager(HttpClient httpClient, IOptions<QdrantSettings> options)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _httpClient.BaseAddress = new Uri(_settings.Host);
    }

    /// <summary>
    /// Get the current (highest) version for a document.
    /// </summary>
    public async Task<int> GetCurrentVersionAsync(string docId)
    {
        try
        {
            var scrollRequest = new
            {
                filter = new
                {
                    must = new[]
                    {
                        new { key = "doc_id", match = new { value = docId } }
                    }
                },
                limit = 1,
                with_payload = new { include = new[] { "version" } }
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"/collections/{_settings.Collection}/points/scroll",
                scrollRequest,
                _jsonOptions);

            if (!response.IsSuccessStatusCode)
                return 0;

            var result = await response.Content.ReadFromJsonAsync<ScrollResponse>(_jsonOptions);

            if (result?.Result?.Points == null || result.Result.Points.Count == 0)
                return 0;

            // En yüksek versiyonu bul
            var maxVersion = result.Result.Points
                .Select(p => p.Payload?.TryGetValue("version", out var v) == true
                    ? v.GetInt32()
                    : 0)
                .DefaultIfEmpty(0)
                .Max();

            return maxVersion;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Delete old versions of a document, keeping the specified number of recent versions.
    /// </summary>
    public async Task CleanOldVersionsAsync(string docId, int keepVersions = 2)
    {
        var currentVersion = await GetCurrentVersionAsync(docId);
        var deleteBeforeVersion = currentVersion - keepVersions;

        if (deleteBeforeVersion <= 0) return;

        try
        {
            var deleteRequest = new
            {
                filter = new
                {
                    must = new object[]
                    {
                        new { key = "doc_id", match = new { value = docId } },
                        new { key = "version", range = new { lt = deleteBeforeVersion } }
                    }
                }
            };

            await _httpClient.PostAsJsonAsync(
                $"/collections/{_settings.Collection}/points/delete",
                deleteRequest,
                _jsonOptions);
        }
        catch
        {
            // Silme başarısız olursa sessizce devam et
        }
    }

    // Response DTOs
    private class ScrollResponse
    {
        [JsonPropertyName("result")]
        public ScrollResult? Result { get; set; }
    }

    private class ScrollResult
    {
        [JsonPropertyName("points")]
        public List<PointResponse>? Points { get; set; }
    }

    private class PointResponse
    {
        [JsonPropertyName("payload")]
        public Dictionary<string, JsonElement>? Payload { get; set; }
    }
}
