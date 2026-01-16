using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using SK_UserGuide.Configuration;
using SK_UserGuide.Services.Abstract;
using SK_UserGuide.Services.Concrete;

namespace SK_UserGuide.Services.Retrieval;
public class HybridRetrievalService : IHybridRetrievalService
{
    private readonly QdrantRestClient _qdrantClient;
    private readonly HttpClient _httpClient;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly QdrantSettings _settings;

    // Configuration constants
    private const int DenseCandidateLimit = 25;
    private const int SparseCandidateLimit = 15;
    private const int MinResultCount = 3;
    private const int MaxResultCount = 7;
    private const int TargetTokenBudget = 800;
    private const int RrfK = 60;
    private const double ThresholdRatio = 0.6;
    private const double AbsoluteMinScore = 0.02;  // Absolute minimum RRF score threshold

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public HybridRetrievalService(
        QdrantRestClient qdrantClient,
        HttpClient httpClient,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IOptions<QdrantSettings> options)
    {
        _qdrantClient = qdrantClient;
        _httpClient = httpClient;
        _embeddingGenerator = embeddingGenerator;
        _settings = options.Value;
        _httpClient.BaseAddress = new Uri(_settings.Host);
    }

    /// <summary>
    /// Perform hybrid retrieval: dense + sparse search with smart selection.
    /// </summary>
    public async Task<RetrievalResult> RetrieveAsync(string question)
    {
        // 1. Generate question embedding
        var embeddings = await _embeddingGenerator.GenerateAsync([question]);
        var queryVector = embeddings.First().Vector.ToArray();

        // 2. Extract keywords for sparse search
        var keywords = KeywordExtractor.Extract(question);

        // 3. Parallel search: dense + sparse
        var denseTask = _qdrantClient.SearchAsync(_settings.Collection, queryVector, DenseCandidateLimit);
        var sparseTask = SearchByKeywordsAsync(keywords);

        await Task.WhenAll(denseTask, sparseTask);

        var denseResults = await denseTask;
        var sparseResults = await sparseTask;

        // 4. RRF Score Fusion
        var fusedResults = FuseWithRRF(denseResults, sparseResults);
        var totalCandidates = fusedResults.Count;

        // 5. Apply dynamic threshold with minimum guarantee
        var thresholded = ApplyDynamicThreshold(fusedResults);

        // 6. Build context with token budget
        var (context, citations, tokensUsed) = BuildContext(thresholded);

        return new RetrievalResult
        {
            Context = context,
            Citations = citations,
            SelectedChunks = thresholded,
            TotalCandidates = totalCandidates,
            TokensUsed = tokensUsed
        };
    }

    /// <summary>
    /// Search by keywords using Qdrant payload filter.
    /// </summary>
    private async Task<List<QdrantRestClient.SearchResult>> SearchByKeywordsAsync(List<string> keywords)
    {
        if (keywords.Count == 0)
            return new List<QdrantRestClient.SearchResult>();

        try
        {
            Console.WriteLine($"[Sparse Search] Keywords: {string.Join(", ", keywords)}");

            var allMatches = new List<(ulong Id, float Score, Dictionary<string, JsonElement>? Payload)>();
            var seenIds = new HashSet<ulong>();

            // Normalize keywords for case-insensitive matching
            var normalizedKeywords = keywords
                .Select(kw => kw.ToLowerInvariant().Trim())
                .Where(kw => kw.Length >= 2) // Minimum 2 characters
                .Distinct()
                .ToList();

            if (normalizedKeywords.Count == 0)
            {
                Console.WriteLine("[Sparse Search] No valid keywords after normalization");
                return new List<QdrantRestClient.SearchResult>();
            }

            Console.WriteLine($"[Sparse Search] Normalized keywords: {string.Join(", ", normalizedKeywords)}");

            // Scroll through collection to get candidates
            // We need to fetch enough candidates to find keyword matches
            string? offset = null;
            int fetchedCount = 0;
            const int scrollBatchSize = 50;
            const int maxFetchLimit = 200; // Safety limit to avoid fetching entire collection

            do
            {
                object scrollRequest = offset == null
                    ? new { limit = scrollBatchSize, with_payload = true, with_vector = false }
                    : new { limit = scrollBatchSize, with_payload = true, with_vector = false, offset = offset };

                var response = await _httpClient.PostAsJsonAsync(
                    $"/collections/{_settings.Collection}/points/scroll",
                    scrollRequest,
                    _jsonOptions);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[Sparse Search] Scroll failed: {response.StatusCode} - {errorContent}");
                    break;
                }

                var result = await response.Content.ReadFromJsonAsync<ScrollResponse>(_jsonOptions);

                if (result?.Result?.Points == null || result.Result.Points.Count == 0)
                    break;

                fetchedCount += result.Result.Points.Count;
                Console.WriteLine($"[Sparse Search] Fetched {result.Result.Points.Count} points (total: {fetchedCount})");

                // Client-side keyword matching (case-insensitive substring search)
                foreach (var point in result.Result.Points)
                {
                    if (seenIds.Contains(point.Id))
                        continue;

                    var text = GetPayloadString(point.Payload, "text");
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    var normalizedText = text.ToLowerInvariant();

                    // Count how many keywords appear in the text (substring match)
                    int matchCount = 0;
                    var matchedKeywords = new List<string>();

                    foreach (var keyword in normalizedKeywords)
                    {
                        if (normalizedText.Contains(keyword))
                        {
                            matchCount++;
                            matchedKeywords.Add(keyword);
                        }
                    }

                    if (matchCount > 0)
                    {
                        seenIds.Add(point.Id);

                        // Score: percentage of keywords matched + boost for multiple matches
                        var score = (float)matchCount / normalizedKeywords.Count;

                        allMatches.Add((point.Id, score, point.Payload));

                        Console.WriteLine($"[Sparse Search] Match found - ID: {point.Id}, Score: {score:F3}, Keywords: {string.Join(", ", matchedKeywords)}");
                    }
                }

                // Stop if we have enough matches or reached fetch limit
                if (allMatches.Count >= SparseCandidateLimit || fetchedCount >= maxFetchLimit)
                    break;

                offset = result.Result.NextPageOffset;

            } while (offset != null);

            Console.WriteLine($"[Sparse Search] Total matches: {allMatches.Count} from {fetchedCount} candidates");

            // Sort by score (most keyword matches first) and limit results
            var topMatches = allMatches
                .OrderByDescending(m => m.Score)
                .ThenBy(m => m.Id) // Stable sort for deterministic results
                .Take(SparseCandidateLimit)
                .Select((m, idx) => new QdrantRestClient.SearchResult
                {
                    Id = m.Id,
                    Score = m.Score,
                    Payload = m.Payload
                })
                .ToList();

            Console.WriteLine($"[Sparse Search] Returning top {topMatches.Count} matches");

            return topMatches;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sparse Search] Exception: {ex.Message}");
            Console.WriteLine($"[Sparse Search] Stack trace: {ex.StackTrace}");
            return new List<QdrantRestClient.SearchResult>();
        }
    }

    /// <summary>
    /// Fuse dense and sparse results using Reciprocal Rank Fusion.
    /// </summary>
    private List<ScoredChunk> FuseWithRRF(
        List<QdrantRestClient.SearchResult> denseResults,
        List<QdrantRestClient.SearchResult> sparseResults)
    {
        var scores = new Dictionary<ulong, (double Score, QdrantRestClient.SearchResult Result)>();

        // Dense ranks
        for (int i = 0; i < denseResults.Count; i++)
        {
            var result = denseResults[i];
            scores[result.Id] = (1.0 / (RrfK + i + 1), result);
        }

        // Sparse ranks (additive for items in both lists)
        for (int i = 0; i < sparseResults.Count; i++)
        {
            var result = sparseResults[i];
            var rrfScore = 1.0 / (RrfK + i + 1);

            if (scores.TryGetValue(result.Id, out var existing))
            {
                // Boost: item appears in both dense and sparse
                scores[result.Id] = (existing.Score + rrfScore, existing.Result);
            }
            else
            {
                scores[result.Id] = (rrfScore, result);
            }
        }

        // Convert to ScoredChunk and sort by fused score
        return scores
            .OrderByDescending(kv => kv.Value.Score)
            .Select(kv => ToScoredChunk(kv.Value.Result, kv.Value.Score))
            .ToList();
    }

    /// <summary>
    /// Apply dynamic threshold based on max score, with absolute minimum filter.
    /// </summary>
    private List<ScoredChunk> ApplyDynamicThreshold(List<ScoredChunk> results)
    {
        if (results.Count == 0) return results;

        var maxScore = results[0].Score;
        var threshold = maxScore * ThresholdRatio;

        // Apply both relative threshold AND absolute minimum score
        var filtered = results
            .Where(r => r.Score >= threshold && r.Score >= AbsoluteMinScore)
            .ToList();

        // Only apply minimum guarantee if filtered results also meet absolute threshold
        if (filtered.Count < MinResultCount)
        {
            // Take top results but still respect absolute minimum
            filtered = results
                .Where(r => r.Score >= AbsoluteMinScore)
                .Take(MinResultCount)
                .ToList();
        }

        return filtered.Take(MaxResultCount).ToList();
    }



    /// <summary>
    /// Build context string with token budget and citations.
    /// </summary>
    private (string Context, List<string> Citations, int TokensUsed) BuildContext(List<ScoredChunk> chunks)
    {
        var context = new StringBuilder();
        var citations = new List<string>();
        var tokensUsed = 0;

        foreach (var chunk in chunks)
        {
            var chunkTokens = chunk.EstimatedTokens;

            // Check budget
            if (tokensUsed + chunkTokens > TargetTokenBudget)
            {
                // Try to fit partial chunk if we're under 80% budget
                if (tokensUsed < TargetTokenBudget * 0.8)
                {
                    var remainingTokens = TargetTokenBudget - tokensUsed;
                    var remainingChars = (int)(remainingTokens * 3.5);
                    var truncatedText = TruncateAtSentence(chunk.Text, remainingChars);

                    if (truncatedText.Length > 50)
                    {
                        var idx = citations.Count + 1;
                        context.AppendLine($"[{idx}] {truncatedText}");
                        citations.Add(FormatCitation(chunk, idx));
                        tokensUsed += (int)(truncatedText.Length / 3.5);
                    }
                }
                break;
            }

            // Add full chunk
            var index = citations.Count + 1;
            context.AppendLine($"[{index}] {chunk.Text}");
            context.AppendLine();
            citations.Add(FormatCitation(chunk, index));
            tokensUsed += chunkTokens;
        }

        return (context.ToString().Trim(), citations, tokensUsed);
    }

    /// <summary>
    /// Format citation for a chunk.
    /// </summary>
    private string FormatCitation(ScoredChunk chunk, int index)
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(chunk.DocName))
            parts.Add(chunk.DocName);
        if (chunk.Page.HasValue)
            parts.Add($"Page {chunk.Page}");
        if (!string.IsNullOrEmpty(chunk.SectionTitle))
            parts.Add(chunk.SectionTitle);

        var location = parts.Count > 0 ? string.Join(" > ", parts) : "Unknown Source";
        return $"[{index}] {location}";
    }

    /// <summary>
    /// Truncate text at a sentence boundary.
    /// </summary>
    private string TruncateAtSentence(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;

        var truncated = text[..maxLength];

        // Find last sentence end
        var lastSentenceEnd = truncated.LastIndexOfAny(new[] { '.', '!', '?' });

        if (lastSentenceEnd > maxLength * 0.5)
        {
            return truncated[..(lastSentenceEnd + 1)];
        }

        // Fall back to word boundary
        var lastSpace = truncated.LastIndexOf(' ');
        if (lastSpace > maxLength * 0.7)
        {
            return truncated[..lastSpace] + "...";
        }

        return truncated + "...";
    }

    /// <summary>
    /// Convert Qdrant search result to ScoredChunk.
    /// </summary>
    private ScoredChunk ToScoredChunk(QdrantRestClient.SearchResult result, double score)
    {
        var payload = result.Payload;
        var text = GetPayloadString(payload, "text");

        // Use actual token count from Qdrant metadata, fallback to character estimation
        var storedTokens = GetPayloadInt(payload, "estimated_tokens");
        var estimatedTokens = storedTokens ?? (int)(text.Length / 3.5);

        return new ScoredChunk
        {
            Id = result.Id,
            Score = score,
            Text = text,
            DocId = GetPayloadString(payload, "doc_id"),
            DocName = GetPayloadString(payload, "doc_name"),
            SectionTitle = GetPayloadString(payload, "section_title"),
            Page = GetPayloadInt(payload, "page"),
            ChunkIndex = GetPayloadInt(payload, "chunk_index"),
            Version = GetPayloadInt(payload, "version"),
            EstimatedTokens = estimatedTokens
        };
    }

    private string GetPayloadString(Dictionary<string, JsonElement>? payload, string key)
    {
        if (payload?.TryGetValue(key, out var element) == true)
        {
            return element.ValueKind == JsonValueKind.String ? element.GetString() ?? "" : "";
        }
        return "";
    }

    private int? GetPayloadInt(Dictionary<string, JsonElement>? payload, string key)
    {
        if (payload?.TryGetValue(key, out var element) == true)
        {
            if (element.ValueKind == JsonValueKind.Number)
                return element.GetInt32();
        }
        return null;
    }

    // DTOs for scroll endpoint
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
