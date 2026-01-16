using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SK_UserGuide.Configuration;

namespace SK_UserGuide.Services.Concrete;
public class OllamaEmbeddingService : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly HttpClient _httpClient;
    private readonly OllamaSettings _settings;
    private readonly SemaphoreSlim _semaphore = new(5);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OllamaEmbeddingService(HttpClient httpClient, IOptions<OllamaSettings> options)
    {
        _httpClient = httpClient;
        _settings = options.Value;
    }

    public EmbeddingGeneratorMetadata Metadata => new("OllamaEmbedding", new Uri(_settings.BaseUrl));

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var valuesList = values.ToList();
        var tasks = valuesList.Select(text => GenerateSingleEmbeddingAsync(text, cancellationToken));
        var embeddings = await Task.WhenAll(tasks);

        return new GeneratedEmbeddings<Embedding<float>>(embeddings);
    }

    private async Task<Embedding<float>> GenerateSingleEmbeddingAsync(string text, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            // Skip empty text
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("Cannot generate embedding for empty text");
            }

            // Use new Ollama /api/embed endpoint with 'input' parameter
            var requestBody = new { model = _settings.EmbeddingModel, input = text };
            var requestJson = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                $"{_settings.BaseUrl}/api/embed",
                content,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            // Parse with case-insensitive options
            var result = JsonSerializer.Deserialize<OllamaEmbedResponse>(responseBody, _jsonOptions);

            // New API returns embeddings as array of arrays
            if (result?.Embeddings == null || result.Embeddings.Count == 0 || result.Embeddings[0].Length == 0)
            {
                var inputPreview = text.Length > 100 ? text.Substring(0, 100) + "..." : text;
                throw new InvalidOperationException($"Ollama returned empty embedding. Input text: '{inputPreview}', Response: {responseBody.Substring(0, Math.Min(200, responseBody.Length))}");
            }

            return new Embedding<float>(result.Embeddings[0]);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(OllamaEmbeddingService) || serviceType == typeof(IEmbeddingGenerator<string, Embedding<float>>))
            return this;
        return null;
    }

    // New Ollama /api/embed response format
    private class OllamaEmbedResponse
    {
        [JsonPropertyName("embeddings")]
        public List<float[]>? Embeddings { get; set; }
    }
}