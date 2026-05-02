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

/// <summary>
/// Hybrid retrieval service combining dense (vector) and sparse (keyword) search.
/// Collection-aware for multi-tenant support.
/// Token budgets are configurable via RagSettings.
/// </summary>
public class HybridRetrievalService : IHybridRetrievalService
{
    private readonly QdrantRestClient _qdrantClient;
    private readonly HttpClient _httpClient;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly QdrantSettings _qdrantSettings;
    private readonly int _targetTokenBudget;

    // Configuration constants
    private const int DenseCandidateLimit = 25;
    private const int SparseCandidateLimit = 15;
    private const int MinResultCount = 3;
    private const int MaxResultCount = 7;
    private const int RrfK = 60;
    private const double ThresholdRatio = 0.6;
    // A single dense or sparse hit starts at 1 / (RrfK + 1), around 0.016.
    // Keep this lower so single-channel matches are not discarded.
    private const double AbsoluteMinScore = 0.005;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public HybridRetrievalService(
        QdrantRestClient qdrantClient,
        HttpClient httpClient,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IOptions<QdrantSettings> qdrantOptions,
        IOptions<RagSettings> ragOptions)
    {
        _qdrantClient = qdrantClient;
        _httpClient = httpClient;
        _embeddingGenerator = embeddingGenerator;
        _qdrantSettings = qdrantOptions.Value;
        _targetTokenBudget = ragOptions.Value.ContextTokenBudget;
        _httpClient.BaseAddress = new Uri(_qdrantSettings.Host);
    }

    /// <summary>
    /// Perform hybrid retrieval: dense + sparse search with smart selection.
    /// </summary>
    /// <param name="question">User question.</param>
    /// <param name="collectionName">Target Qdrant collection.</param>
    public async Task<RetrievalResult> RetrieveAsync(string question, string collectionName)
    {
        if (!await _qdrantClient.CollectionExistsAsync(collectionName))
            throw new QdrantCollectionNotFoundException(collectionName);

        // 1. Generate question embedding
        var embeddings = await _embeddingGenerator.GenerateAsync([question]);
        var queryVector = embeddings.First().Vector.ToArray();

        // 2. Extract keywords for sparse search
        var keywords = KeywordExtractor.Extract(question);

        // 3. Parallel search: dense + sparse
        var denseTask = _qdrantClient.SearchAsync(collectionName, queryVector, DenseCandidateLimit);
        var sparseTask = SearchByKeywordsAsync(keywords, collectionName);

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
    /// Search by keywords using Qdrant full-text index filter (server-side).
    /// Uses text_any for OR matching - returns results containing ANY keyword.
    /// </summary>
    private async Task<List<QdrantRestClient.SearchResult>> SearchByKeywordsAsync(
        List<string> keywords, string collectionName)
    {
        if (keywords.Count == 0)
            return new List<QdrantRestClient.SearchResult>();

        try
        {
            // Normalize keywords
            var normalizedKeywords = keywords
                .Select(kw => kw.ToLowerInvariant().Trim())
                .Where(kw => kw.Length >= 2)
                .Distinct()
                .ToList();

            if (normalizedKeywords.Count == 0)
            {
                Console.WriteLine("[Sparse Search] No valid keywords after normalization");
                return new List<QdrantRestClient.SearchResult>();
            }

            Console.WriteLine($"[Sparse Search] Keywords: {string.Join(", ", normalizedKeywords)}");

            var points = await ScrollByKeywordFieldAsync(
                collectionName,
                "text_normalized",
                normalizedKeywords.Select(SearchTextNormalizer.ToSearchText).Distinct().ToList());

            if (points.Count == 0)
            {
                points = await ScrollByKeywordFieldAsync(collectionName, "text", normalizedKeywords);
            }

            if (points.Count == 0)
            {
                Console.WriteLine("[Sparse Search] No matches found");
                return new List<QdrantRestClient.SearchResult>();
            }

            Console.WriteLine($"[Sparse Search] Found {points.Count} matches via full-text index");

            // Calculate keyword match score for each result (for RRF fusion)
            var scoredResults = new List<QdrantRestClient.SearchResult>();

            foreach (var point in points)
            {
                var text = SearchTextNormalizer.ToSearchText(GetPayloadString(point.Payload, "text"));

                // Count how many keywords appear (for scoring)
                var normalizedForScoring = normalizedKeywords
                    .Select(SearchTextNormalizer.ToSearchText)
                    .Distinct()
                    .ToList();
                var matchCount = normalizedForScoring.Count(kw => text.Contains(kw));
                var score = (float)matchCount / normalizedForScoring.Count;

                scoredResults.Add(new QdrantRestClient.SearchResult
                {
                    Id = point.Id,
                    Score = score,
                    Payload = point.Payload
                });
            }

            // Sort by score (most keyword matches first)
            var sortedResults = scoredResults
                .OrderByDescending(r => r.Score)
                .Take(SparseCandidateLimit)
                .ToList();

            Console.WriteLine($"[Sparse Search] Returning {sortedResults.Count} scored results");

            return sortedResults;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sparse Search] Exception: {ex.Message}");
            return new List<QdrantRestClient.SearchResult>();
        }
    }

    private async Task<List<ScrollPoint>> ScrollByKeywordFieldAsync(
        string collectionName,
        string fieldName,
        List<string> keywords)
    {
        var keywordQuery = string.Join(" ", keywords
            .Where(kw => !string.IsNullOrWhiteSpace(kw))
            .Distinct());

        if (string.IsNullOrWhiteSpace(keywordQuery))
            return new List<ScrollPoint>();

        var scrollRequest = new
        {
            filter = new
            {
                must = new[]
                {
                    new
                    {
                        key = fieldName,
                        match = new { text_any = keywordQuery }
                    }
                }
            },
            limit = SparseCandidateLimit,
            with_payload = true,
            with_vector = false
        };

        var response = await _httpClient.PostAsJsonAsync(
            $"/collections/{collectionName}/points/scroll",
            scrollRequest,
            _jsonOptions);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[Sparse Search] {fieldName} filter failed: {response.StatusCode} - {errorContent}");
            return new List<ScrollPoint>();
        }

        var result = await response.Content.ReadFromJsonAsync<ScrollResponse>(_jsonOptions);
        return result?.Result?.Points ?? new List<ScrollPoint>();
    }

    private ulong? ParseNextPageOffset(JsonElement? element)
    {
        if (element == null || element.Value.ValueKind == JsonValueKind.Null)
            return null;

        return element.Value.ValueKind switch
        {
            JsonValueKind.Number => element.Value.GetUInt64(),
            JsonValueKind.String when ulong.TryParse(element.Value.GetString(), out var id) => id,
            _ => null
        };
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
            if (tokensUsed + chunkTokens > _targetTokenBudget)
            {
                // Try to fit partial chunk if we're under 80% budget
                if (tokensUsed < _targetTokenBudget * 0.8)
                {
                    var remainingTokens = _targetTokenBudget - tokensUsed;
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
        public JsonElement? NextPageOffset { get; set; }
    }

    private class ScrollPoint
    {
        [JsonPropertyName("id")]
        public ulong Id { get; set; }

        [JsonPropertyName("payload")]
        public Dictionary<string, JsonElement>? Payload { get; set; }
    }
}
