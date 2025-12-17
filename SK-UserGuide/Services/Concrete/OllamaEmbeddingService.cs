using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel;
using System.Text.Json;

namespace SK_UserGuide.Services.Concrete;

public class OllamaEmbeddingService : ITextEmbeddingGenerationService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(5); // Bounded concurrency for batch performance

    public OllamaEmbeddingService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _baseUrl = configuration["Ollama:BaseUrl"];
        _model = configuration["Ollama:EmbeddingModel"];
    }

    public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

    public async Task<IList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(IList<string> data, CancellationToken cancellationToken = default)
    {
        var tasks = data.Select(text => GenerateEmbeddingAsync(text, cancellationToken)).ToList();
        return await Task.WhenAll(tasks);
    }

    public async Task<IList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(IList<string> data, Kernel? kernel, CancellationToken cancellationToken = default)
    {
        return await GenerateEmbeddingsAsync(data, cancellationToken);
    }

    private async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var request = new { model = _model, prompt = text };
            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/embeddings", request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(cancellationToken);
            return new ReadOnlyMemory<float>(result.Embedding);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private class OllamaEmbeddingResponse
    {
        public float[] Embedding { get; set; }
    }
}