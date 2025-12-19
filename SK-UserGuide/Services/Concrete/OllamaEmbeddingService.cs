using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using SK_UserGuide.Configuration;

namespace SK_UserGuide.Services.Concrete;

/// <summary>
/// Custom embedding service that uses Ollama's embedding API.
/// </summary>
public class OllamaEmbeddingService : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly HttpClient _httpClient;
    private readonly OllamaSettings _settings;
    private readonly SemaphoreSlim _semaphore = new(5);

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
            var request = new { model = _settings.EmbeddingModel, prompt = text };
            var response = await _httpClient.PostAsJsonAsync(
                $"{_settings.BaseUrl}/api/embeddings",
                request,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(cancellationToken);

            if (result?.Embedding == null)
                throw new InvalidOperationException("Ollama returned null embedding");

            return new Embedding<float>(result.Embedding);
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

    private class OllamaEmbeddingResponse
    {
        public float[]? Embedding { get; set; }
    }
}