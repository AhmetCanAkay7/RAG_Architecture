using System.Text.RegularExpressions;
using SK_UserGuide.Services.Retrieval;

namespace SK_UserGuide.Services.LLM;

/// <summary>
/// Compresses retrieved context by extracting relevant sentences.
/// Uses extractive compression (no LLM call required).
/// </summary>
public class ContextCompressor
{
    private const int TargetTokenBudget = 800;
    private const int MaxSentencesPerChunk = 5;
    private const int MinSentenceLength = 15;

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
                sentences, questionKeywords, MaxSentencesPerChunk);

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
                    Language = "EN", // Default to English
                    OriginalScore = chunk.Score
                });
            }

            // Token budget check
            if (result.EstimatedTokens >= TargetTokenBudget)
                break;
        }

        // 4. Merge consecutive chunks from same section
        result.Items = MergeConsecutiveChunks(result.Items);

        return result;
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
        int maxCount)
    {
        if (sentences.Count <= maxCount)
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
            "this", "that", "it", "they", "we", "you", "i", "my", "your"
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
            // Use first 100 chars as signature for dedup
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

            // Merge if same doc + section
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

        // Re-index
        for (int i = 0; i < merged.Count; i++)
        {
            merged[i] = merged[i] with { Index = i + 1 };
        }

        return merged;
    }
}
