using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SK_UserGuide.Configuration;
using SK_UserGuide.Services.Retrieval;

namespace SK_UserGuide.Services.LLM;

/// <summary>
/// Compresses retrieved context by extracting relevant sentences.
/// Uses extractive compression (no LLM call required).
/// Token budgets are configurable via RagSettings.
/// </summary>
public class ContextCompressor
{
    private readonly int _targetTokenBudget;
    private readonly int _maxSentencesPerChunk;
    private readonly int _summaryMaxSentences;
    private readonly int _summaryTargetTokens;
    private const int MinSentenceLength = 15;

    public ContextCompressor(IOptions<RagSettings> ragSettings)
    {
        var settings = ragSettings.Value;
        _targetTokenBudget = settings.CompressorTokenBudget;
        _maxSentencesPerChunk = settings.MaxSentencesPerChunk;
        _summaryMaxSentences = settings.SummaryMaxSentences;
        _summaryTargetTokens = settings.SummaryTargetTokens;
    }

    /// <summary>
    /// Compress chunks by extracting relevant sentences.
    /// </summary>
    public CompressedContext Compress(
        List<ScoredChunk> chunks,
        string question,
        string questionLanguage)
    {
        var result = new CompressedContext();
        var questionKeywords = ExtractKeywords(question);

        // 1. Sort by relevance score
        var sortedChunks = chunks.OrderByDescending(c => c.Score).ToList();

        // 2. Deduplicate chunks
        var dedupedChunks = DeduplicateChunks(sortedChunks);

        // 3. Extract relevant sentences from each chunk
        foreach (var chunk in dedupedChunks)
        {
            var sentences = SplitToSentences(chunk.Text);
            var relevantSentences = SelectRelevantSentences(
                sentences, questionKeywords, _maxSentencesPerChunk, chunk.Text);

            if (relevantSentences.Count > 0)
            {
                var compressedText = string.Join(" ", relevantSentences);

                result.Items.Add(new ContextItem
                {
                    Index = result.Items.Count + 1,
                    Text = compressedText,
                    DocName = chunk.DocName,
                    Page = chunk.Page,
                    Section = chunk.SectionTitle,
                    Language = "EN",
                    OriginalScore = chunk.Score
                });
            }

            // Token budget check
            if (result.EstimatedTokens >= _targetTokenBudget)
                break;
        }

        // 4. Merge consecutive chunks from same section
        result.Items = MergeConsecutiveChunks(result.Items);

        // 5. Generate enhanced summary
        result.Summary = GenerateSummary(result.Items, questionKeywords);

        return result;
    }

    /// <summary>
    /// Generate summary with configurable sentence count and token budget.
    /// </summary>
    private string GenerateSummary(List<ContextItem> items, HashSet<string> questionKeywords)
    {
        if (items.Count == 0)
            return "No relevant information found.";

        var allSentences = new List<(string Sentence, double Score, int Index)>();

        // Score all sentences
        for (int i = 0; i < items.Count; i++)
        {
            var sentences = SplitToSentences(items[i].Text);
            foreach (var sentence in sentences)
            {
                var score = CalculateKeywordOverlap(sentence, questionKeywords);
                // Boost first sentence of each chunk (topic sentence)
                var isFirstSentence = sentence == sentences.FirstOrDefault();
                var adjustedScore = isFirstSentence ? score + 0.1 : score;
                
                allSentences.Add((sentence, adjustedScore, i + 1));
            }
        }

        // Take top sentences within token budget
        var selectedSentences = new List<(string Sentence, int Index)>();
        var currentTokens = 0;

        foreach (var item in allSentences.OrderByDescending(x => x.Score))
        {
            var sentenceTokens = EstimateTokens(item.Sentence);
            
            if (currentTokens + sentenceTokens > _summaryTargetTokens)
                continue;
                
            if (selectedSentences.Count >= _summaryMaxSentences)
                break;

            selectedSentences.Add((item.Sentence, item.Index));
            currentTokens += sentenceTokens;
        }

        // Order by original index for coherent reading
        var orderedSentences = selectedSentences
            .OrderBy(x => x.Index)
            .Select(x => $"{x.Sentence} [{x.Index}]")
            .ToList();

        return string.Join(" ", orderedSentences);
    }

    /// <summary>
    /// Estimate token count (rough: 1 token ≈ 4 chars).
    /// </summary>
    private static int EstimateTokens(string text)
    {
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    private List<string> SplitToSentences(string text)
    {
        // Split on sentence boundaries
        var sentences = Regex.Split(text, @"(?<=[.!?])\s+")
            .Where(s => s.Length >= MinSentenceLength)
            .Select(s => s.Trim())
            .ToList();

        // If no sentence boundaries found, split on newlines
        if (sentences.Count <= 1 && text.Contains('\n'))
        {
            sentences = text.Split('\n')
                .Where(s => s.Trim().Length >= MinSentenceLength)
                .Select(s => s.Trim())
                .ToList();
        }

        return sentences;
    }

    private List<string> SelectRelevantSentences(
        List<string> sentences,
        HashSet<string> keywords,
        int maxCount,
        string originalText)
    {
        if (sentences.Count <= maxCount)
            return sentences;

        // Check if content should be preserved intact (lists, code, tables)
        if (ShouldPreserveIntact(sentences, originalText))
            return sentences;

        // Score sentences by keyword overlap
        var scored = sentences.Select(s => new
        {
            Sentence = s,
            Score = CalculateKeywordOverlap(s, keywords)
        })
        .OrderByDescending(x => x.Score)
        .Take(maxCount)
        .Select(x => x.Sentence)
        .ToList();

        // Preserve original order
        return sentences.Where(s => scored.Contains(s)).ToList();
    }

    private bool ShouldPreserveIntact(List<string> sentences, string originalText)
    {
        if (IsNumberedList(sentences))
            return true;

        if (IsCodeBlock(originalText))
            return true;

        if (IsTable(sentences))
            return true;

        return false;
    }

    private bool IsNumberedList(List<string> sentences)
    {
        if (sentences.Count < 3)
            return false;

        var listPatternCount = sentences.Count(s =>
            Regex.IsMatch(s.TrimStart(), @"^(Step\s*\d+[:\.\)]|\d+[\.\)]\s|[a-z][\.\)]\s|[-•*]\s)", RegexOptions.IgnoreCase));

        return listPatternCount >= sentences.Count * 0.5;
    }

    private bool IsCodeBlock(string text)
    {
        if (text.Contains("```"))
            return true;

        var braceCount = text.Count(c => c == '{' || c == '}');
        if (braceCount >= 4)
            return true;

        var lines = text.Split('\n');
        var indentedCount = lines.Count(l => l.StartsWith("    ") || l.StartsWith("\t"));
        if (indentedCount >= lines.Length * 0.5 && indentedCount >= 3)
            return true;

        return false;
    }

    private bool IsTable(List<string> sentences)
    {
        var tableRowCount = sentences.Count(s =>
            s.Contains('|') && s.Count(c => c == '|') >= 2);

        return tableRowCount >= 2;
    }

    private double CalculateKeywordOverlap(string sentence, HashSet<string> keywords)
    {
        var sentenceWords = Tokenize(sentence.ToLowerInvariant());
        var matches = sentenceWords.Count(w => keywords.Contains(w));
        return (double)matches / Math.Max(keywords.Count, 1);
    }

    private HashSet<string> ExtractKeywords(string text)
    {
        var stopwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "is", "are", "was", "were", "be", "been",
            "to", "of", "and", "or", "in", "on", "at", "for", "with",
            "what", "how", "why", "when", "where", "which", "who",
            "this", "that", "it", "they", "we", "you", "i", "my", "your","Can",
            "may","might","these","those"
        };

        return Tokenize(text.ToLowerInvariant())
            .Where(w => w.Length > 2 && !stopwords.Contains(w))
            .ToHashSet();
    }

    private IEnumerable<string> Tokenize(string text)
    {
        return Regex.Split(text, @"\W+")
            .Where(w => !string.IsNullOrWhiteSpace(w));
    }

    private List<ScoredChunk> DeduplicateChunks(List<ScoredChunk> chunks)
    {
        var seen = new HashSet<string>();
        var result = new List<ScoredChunk>();

        foreach (var chunk in chunks)
        {
            var signature = chunk.Text.Length > 100
                ? chunk.Text[..100]
                : chunk.Text;

            var normalized = Regex.Replace(signature.ToLowerInvariant(), @"\s+", " ");

            if (!seen.Contains(normalized))
            {
                seen.Add(normalized);
                result.Add(chunk);
            }
        }

        return result;
    }

    private List<ContextItem> MergeConsecutiveChunks(List<ContextItem> items)
    {
        if (items.Count <= 1) return items;

        var merged = new List<ContextItem>();
        var current = items[0];

        for (int i = 1; i < items.Count; i++)
        {
            var next = items[i];

            if (current.DocName == next.DocName &&
                current.Section == next.Section &&
                current.Language == next.Language)
            {
                current = current with
                {
                    Text = current.Text + "\n" + next.Text
                };
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }
        merged.Add(current);

        for (int i = 0; i < merged.Count; i++)
        {
            merged[i] = merged[i] with { Index = i + 1 };
        }

        return merged;
    }
}
