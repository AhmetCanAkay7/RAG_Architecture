using System.Text;
using System.Text.RegularExpressions;
using Microsoft.ML.Tokenizers;

namespace SK_UserGuide.Services.Ingestion;

/// <summary>
/// Structural chunking algorithm optimized for user guides.
/// Features:
/// - Heading/paragraph/step list/table detection
/// - Token-based budgeting using real Tiktoken tokenizer (150-350 tokens target)
/// - Overlap support (50 tokens)
/// - Step list preservation (keeps steps together)
/// - Context injection ([Section: X] > content)
/// - Atomic table handling
/// </summary>
public class StructuralChunker
{
    // Token limits for chunk sizing
    private const int TargetTokenMin = 150;
    private const int TargetTokenMax = 350;
    private const int OverlapTokens = 50;
    private const int MinChunkTokens = 30;

    // Table tolerance: allow 50% overflow to keep tables atomic
    private const double TableTokenTolerance = 1.5;

    private readonly TextCleaner _textCleaner;
    private readonly Tokenizer _tokenizer;
    private readonly HierarchicalContextBuilder _contextBuilder;

    public StructuralChunker(TextCleaner textCleaner)
    {
        _textCleaner = textCleaner;

        // Initialize Tiktoken tokenizer with cl100k_base encoding
        // Compatible with nomic-embed-text embedding model
        _tokenizer = TiktokenTokenizer.CreateForEncoding("cl100k_base");

        // Initialize hierarchical context builder for breadcrumb-style section tracking
        _contextBuilder = new HierarchicalContextBuilder();
    }

    /// <summary>
    /// Count tokens using the real Tiktoken tokenizer.
    /// </summary>
    private int CountTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return _tokenizer.CountTokens(text);
    }

    /// <summary>
    /// Chunk a document into semantically meaningful pieces.
    /// </summary>
    public List<ChunkResult> Chunk(ExtractedDocument document)
    {
        var chunks = new List<ChunkResult>();

        // Clean all pages
        var cleanedPages = document.Pages
            .Select(p => new PageContent(p.PageNumber, _textCleaner.Clean(p.Text)))
            .ToList();
        var cleanedDoc = document with { Pages = cleanedPages };

        // Parse structural blocks
        var blocks = ParseStructuralBlocks(cleanedDoc);

        var currentChunk = new ChunkBuilder(CountTokens);
        _contextBuilder.Reset(); // Reset for new document
        int chunkIndex = 0;

        foreach (var block in blocks)
        {
            // Skip empty blocks
            if (block.Type == BlockType.Empty || string.IsNullOrWhiteSpace(block.Text))
                continue;

            // === HEADING BLOCK ===
            // STRICT SEPARATION: Always finalize current chunk and start new section
            if (block.Type == BlockType.Heading)
            {
                // Always finalize current chunk if it has content (no matter how short)
                if (currentChunk.HasContent)
                {
                    chunks.Add(currentChunk.Build(chunkIndex++, _contextBuilder.GetFullSectionPath()));
                    currentChunk = new ChunkBuilder(CountTokens);
                }

                // Update hierarchical context with new heading
                _contextBuilder.ProcessHeading(block.Text);

                // Start new section with hierarchical context prefix
                currentChunk.AppendWithContext(block, _contextBuilder.BuildContextPrefix(), _contextBuilder.GetFullSectionPath());
                continue;
            }

            // === TABLE BLOCK ===
            // Atomic handling: try to keep table as single unit
            if (block.Type == BlockType.Table)
            {
                // Finalize current chunk first
                if (currentChunk.HasContent)
                {
                    chunks.Add(currentChunk.Build(chunkIndex++, _contextBuilder.GetFullSectionPath()));
                    currentChunk = new ChunkBuilder(CountTokens);
                }

                var tableTokens = CountTokens(block.Text);

                // Keep table atomic if within tolerance (350 * 1.5 = 525 tokens)
                if (tableTokens <= TargetTokenMax * TableTokenTolerance)
                {
                    chunks.Add(CreateTableChunk(block, _contextBuilder.BuildContextPrefix(), _contextBuilder.GetFullSectionPath(), chunkIndex++));
                }
                else
                {
                    // Table too large - split by rows while preserving header
                    var tableChunks = SplitLargeTable(block, _contextBuilder.BuildContextPrefix(), _contextBuilder.GetFullSectionPath(), ref chunkIndex);
                    chunks.AddRange(tableChunks);
                }
                continue;
            }

            // === STEP LIST BLOCK ===
            // Keep related steps together
            if (block.Type == BlockType.StepList)
            {
                if (currentChunk.HasContent)
                {
                    chunks.Add(currentChunk.Build(chunkIndex++, _contextBuilder.GetFullSectionPath()));
                    currentChunk = new ChunkBuilder(CountTokens);
                }

                var stepChunks = ChunkStepList(block, _contextBuilder.BuildContextPrefix(), _contextBuilder.GetFullSectionPath(), ref chunkIndex);
                chunks.AddRange(stepChunks);
                continue;
            }

            // === PARAGRAPH BLOCK ===
            // Token budgeting with overlap and context injection
            var blockTokens = CountTokens(block.Text);
            var projectedTokens = currentChunk.TokenCount + blockTokens;

            // Single block exceeds max - split by sentences
            if (blockTokens > TargetTokenMax)
            {
                if (currentChunk.HasContent)
                {
                    chunks.Add(currentChunk.Build(chunkIndex++, _contextBuilder.GetFullSectionPath()));
                    currentChunk = new ChunkBuilder(CountTokens);
                }

                var splitChunks = SplitLongParagraph(block.Text, _contextBuilder.BuildContextPrefix(), _contextBuilder.GetFullSectionPath(), block.Page, ref chunkIndex);
                chunks.AddRange(splitChunks);
                continue;
            }

            // Would exceed budget - finalize and start new with overlap
            if (projectedTokens > TargetTokenMax && currentChunk.HasContent)
            {
                chunks.Add(currentChunk.Build(chunkIndex++, _contextBuilder.GetFullSectionPath()));

                // Start new chunk with overlap
                currentChunk = new ChunkBuilder(CountTokens);
                if (chunks.Count > 0)
                {
                    var overlapText = GetOverlapText(chunks.Last().Text);
                    if (!string.IsNullOrEmpty(overlapText))
                    {
                        currentChunk.AppendOverlap(overlapText);
                    }
                }

                // Inject context for new chunk
                currentChunk.AppendWithContext(block, _contextBuilder.BuildContextPrefix(), _contextBuilder.GetFullSectionPath());
            }
            else
            {
                // Fits in current chunk
                currentChunk.AppendWithContext(block, _contextBuilder.BuildContextPrefix(), _contextBuilder.GetFullSectionPath());
            }
        }

        // Save last chunk
        if (currentChunk.HasContent)
        {
            chunks.Add(currentChunk.Build(chunkIndex, _contextBuilder.GetFullSectionPath()));
        }

        // Merge very short chunks (but not across section boundaries)
        chunks = MergeShortChunks(chunks);

        return chunks;
    }

    /// <summary>
    /// Parse document into structural blocks (headings, paragraphs, step lists, tables).
    /// </summary>
    private List<StructuralBlock> ParseStructuralBlocks(ExtractedDocument document)
    {
        var blocks = new List<StructuralBlock>();

        foreach (var page in document.Pages)
        {
            var lines = page.Text.Split('\n');
            var currentBlock = new StringBuilder();
            BlockType currentType = BlockType.Paragraph;

            foreach (var line in lines)
            {
                var detectedType = DetectLineType(line);

                // Split block when type changes
                if (ShouldSplitBlock(currentType, detectedType) && currentBlock.Length > 0)
                {
                    blocks.Add(new StructuralBlock(currentType, currentBlock.ToString().Trim(), page.PageNumber));
                    currentBlock.Clear();
                }

                if (detectedType != BlockType.Empty)
                {
                    currentType = detectedType;
                }

                currentBlock.AppendLine(line);
            }

            if (currentBlock.Length > 0)
            {
                blocks.Add(new StructuralBlock(currentType, currentBlock.ToString().Trim(), page.PageNumber));
            }
        }

        return blocks;
    }

    /// <summary>
    /// Determine if we should start a new block.
    /// </summary>
    private bool ShouldSplitBlock(BlockType currentType, BlockType newType)
    {
        // Heading always starts new block
        if (newType == BlockType.Heading) return true;

        // Heading should be single line - split when paragraph follows
        if (currentType == BlockType.Heading && newType == BlockType.Paragraph) return true;

        // Table transitions
        if (currentType == BlockType.Table && newType != BlockType.Table) return true;
        if (currentType != BlockType.Table && newType == BlockType.Table) return true;

        // List transitions
        if (currentType == BlockType.StepList && newType == BlockType.Paragraph) return true;
        if (currentType == BlockType.Paragraph && newType == BlockType.StepList) return true;

        return false;
    }

    /// <summary>
    /// Detect the type of a text line.
    /// </summary>
    private BlockType DetectLineType(string line)
    {
        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed)) return BlockType.Empty;

        // Markdown headings
        if (trimmed.StartsWith('#')) return BlockType.Heading;

        // Numbered headings: "1. Introduction", "2.1 Sub Heading"
        if (Regex.IsMatch(trimmed, @"^(\d+\.)+\s+[A-Z][a-z]") && trimmed.Length < 80)
            return BlockType.Heading;

        // All caps headings (common in PDFs)
        if (trimmed.Length > 3 && trimmed.Length < 60 &&
            trimmed.All(c => char.IsUpper(c) || char.IsWhiteSpace(c) || char.IsDigit(c) || ".-:".Contains(c)))
            return BlockType.Heading;

        // Markdown table detection (lines that start and end with |)
        if (trimmed.StartsWith('|') && trimmed.EndsWith('|'))
            return BlockType.Table;

        // Table separator line (|---|---|)
        if (Regex.IsMatch(trimmed, @"^\|[\s\-:]+\|"))
            return BlockType.Table;

        // Step/list patterns
        var stepPatterns = new[]
        {
            @"^Step\s*\d+[:\.\s]",           // "Step 1:", "Step 2."
            @"^\d+[\.\)]\s+\S",               // "1. ...", "1) ..."
            @"^[a-z][\.\)]\s+\S",             // "a. ...", "a) ..."
            @"^[•\-\*]\s+\S",                 // "• ...", "- ...", "* ..."
            @"^➤\s+\S"                        // "➤ ..."
        };

        if (stepPatterns.Any(p => Regex.IsMatch(trimmed, p, RegexOptions.IgnoreCase)))
            return BlockType.StepList;

        return BlockType.Paragraph;
    }

    /// <summary>
    /// Create a chunk for a table block with context injection.
    /// </summary>
    private ChunkResult CreateTableChunk(StructuralBlock block, string contextPrefix, string sectionPath, int index)
    {
        var textWithContext = string.IsNullOrEmpty(contextPrefix)
            ? block.Text
            : contextPrefix + block.Text;

        return new ChunkResult
        {
            Index = index,
            Text = textWithContext,
            SectionTitle = sectionPath,
            Page = block.Page,
            EstimatedTokens = CountTokens(textWithContext),
            HasOverlap = false
        };
    }

    /// <summary>
    /// Split a large table by rows while preserving header.
    /// </summary>
    private List<ChunkResult> SplitLargeTable(StructuralBlock block, string contextPrefix, string sectionPath, ref int chunkIndex)
    {
        var chunks = new List<ChunkResult>();
        var lines = block.Text.Split('\n');

        // Find header (first row + separator)
        var headerLines = new List<string>();
        var dataLines = new List<string>();
        var inHeader = true;

        foreach (var line in lines)
        {
            if (inHeader)
            {
                headerLines.Add(line);
                // Separator line marks end of header
                if (Regex.IsMatch(line.Trim(), @"^\|[\s\-:]+\|"))
                {
                    inHeader = false;
                }
            }
            else
            {
                dataLines.Add(line);
            }
        }

        var header = string.Join("\n", headerLines);
        var headerTokens = CountTokens(header);
        var currentRows = new List<string>();
        var currentTokens = headerTokens;

        foreach (var row in dataLines)
        {
            var rowTokens = CountTokens(row);

            if (currentTokens + rowTokens > TargetTokenMax && currentRows.Count > 0)
            {
                // Create chunk with header + current rows
                var tableText = header + "\n" + string.Join("\n", currentRows);
                var textWithContext = string.IsNullOrEmpty(contextPrefix)
                    ? tableText
                    : contextPrefix + tableText;

                chunks.Add(new ChunkResult
                {
                    Index = chunkIndex++,
                    Text = textWithContext,
                    SectionTitle = sectionPath,
                    Page = block.Page,
                    EstimatedTokens = CountTokens(textWithContext),
                    HasOverlap = false
                });

                currentRows.Clear();
                currentTokens = headerTokens;
            }

            currentRows.Add(row);
            currentTokens += rowTokens;
        }

        // Last chunk
        if (currentRows.Count > 0)
        {
            var tableText = header + "\n" + string.Join("\n", currentRows);
            var textWithContext = string.IsNullOrEmpty(contextPrefix)
                ? tableText
                : contextPrefix + tableText;

            chunks.Add(new ChunkResult
            {
                Index = chunkIndex++,
                Text = textWithContext,
                SectionTitle = sectionPath,
                Page = block.Page,
                EstimatedTokens = CountTokens(textWithContext),
                HasOverlap = false
            });
        }

        return chunks;
    }

    /// <summary>
    /// Chunk a step list while trying to keep related steps together.
    /// </summary>
    private List<ChunkResult> ChunkStepList(StructuralBlock block, string contextPrefix, string sectionPath, ref int chunkIndex)
    {
        var chunks = new List<ChunkResult>();
        var steps = ParseIndividualSteps(block.Text);

        if (steps.Count == 0)
        {
            var textWithContext = string.IsNullOrEmpty(contextPrefix)
                ? block.Text
                : contextPrefix + block.Text;
            var tokens = CountTokens(textWithContext);
            chunks.Add(new ChunkResult
            {
                Index = chunkIndex++,
                Text = textWithContext,
                SectionTitle = sectionPath,
                Page = block.Page,
                EstimatedTokens = tokens,
                HasOverlap = false
            });
            return chunks;
        }

        var currentSteps = new List<string>();
        int currentTokens = 0;
        var contextTokens = CountTokens(contextPrefix);

        foreach (var step in steps)
        {
            var stepTokens = CountTokens(step);

            // Single step too large
            if (stepTokens + contextTokens > TargetTokenMax)
            {
                if (currentSteps.Any())
                {
                    chunks.Add(CreateStepChunk(currentSteps, sectionPath, chunkIndex++, block.Page, contextPrefix));
                    currentSteps.Clear();
                    currentTokens = 0;
                }

                var textWithContext = contextPrefix + step;
                chunks.Add(new ChunkResult
                {
                    Index = chunkIndex++,
                    Text = textWithContext,
                    SectionTitle = sectionPath,
                    Page = block.Page,
                    EstimatedTokens = CountTokens(textWithContext),
                    HasOverlap = false
                });
                continue;
            }

            // Would exceed budget
            if (currentTokens + stepTokens + contextTokens > TargetTokenMax && currentSteps.Any())
            {
                chunks.Add(CreateStepChunk(currentSteps, sectionPath, chunkIndex++, block.Page, contextPrefix));
                currentSteps.Clear();
                currentTokens = 0;
            }

            currentSteps.Add(step);
            currentTokens += stepTokens;
        }

        if (currentSteps.Any())
        {
            chunks.Add(CreateStepChunk(currentSteps, sectionPath, chunkIndex++, block.Page, contextPrefix));
        }

        return chunks;
    }

    /// <summary>
    /// Parse step list text into individual steps.
    /// </summary>
    private List<string> ParseIndividualSteps(string text)
    {
        var stepPattern = @"(?=(?:Step\s*\d+[:\.\s]|\d+[\.\)]\s|[•\-\*➤]\s))";
        var parts = Regex.Split(text, stepPattern, RegexOptions.IgnoreCase)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToList();

        return parts.Any() ? parts : new List<string> { text };
    }

    /// <summary>
    /// Create a chunk from a list of steps with context injection.
    /// </summary>
    private ChunkResult CreateStepChunk(IEnumerable<string> steps, string section, int index, int? page, string contextPrefix)
    {
        var stepsText = string.Join("\n", steps);
        var text = contextPrefix + stepsText;
        return new ChunkResult
        {
            Index = index,
            Text = text,
            SectionTitle = section,
            Page = page,
            EstimatedTokens = CountTokens(text),
            HasOverlap = false
        };
    }

    /// <summary>
    /// Split a long paragraph into multiple chunks by sentences.
    /// </summary>
    private List<ChunkResult> SplitLongParagraph(string text, string contextPrefix, string sectionPath, int? page, ref int chunkIndex)
    {
        var chunks = new List<ChunkResult>();
        var sentences = SplitIntoSentences(text);
        var contextTokens = CountTokens(contextPrefix);

        if (sentences.Count == 0)
        {
            return SplitByCharacters(text, contextPrefix, sectionPath, page, ref chunkIndex);
        }

        var currentText = new StringBuilder();
        var currentTokens = contextTokens;
        string lastOverlap = "";
        var hasOverlap = false;

        foreach (var sentence in sentences)
        {
            var sentenceTokens = CountTokens(sentence);

            // Single sentence too large
            if (sentenceTokens + contextTokens > TargetTokenMax)
            {
                if (currentText.Length > 0)
                {
                    var chunkText = contextPrefix + currentText.ToString().Trim();
                    chunks.Add(CreateParagraphChunk(chunkText, sectionPath, page, chunkIndex++, hasOverlap));
                    lastOverlap = GetOverlapText(currentText.ToString());
                    currentText.Clear();
                    currentTokens = contextTokens;
                }

                var splitChunks = SplitByCharacters(sentence, contextPrefix, sectionPath, page, ref chunkIndex);
                chunks.AddRange(splitChunks);
                if (splitChunks.Count > 0)
                {
                    lastOverlap = GetOverlapText(splitChunks.Last().Text);
                }
                hasOverlap = true;
                continue;
            }

            // Would exceed budget
            if (currentTokens + sentenceTokens > TargetTokenMax && currentText.Length > 0)
            {
                var chunkText = contextPrefix + currentText.ToString().Trim();
                chunks.Add(CreateParagraphChunk(chunkText, sectionPath, page, chunkIndex++, hasOverlap));
                lastOverlap = GetOverlapText(currentText.ToString());

                currentText.Clear();
                currentTokens = contextTokens;

                // Add overlap
                if (!string.IsNullOrEmpty(lastOverlap))
                {
                    currentText.Append(lastOverlap);
                    currentText.Append(" ");
                    currentTokens += CountTokens(lastOverlap);
                    hasOverlap = true;
                }
            }

            if (currentText.Length > 0) currentText.Append(" ");
            currentText.Append(sentence);
            currentTokens += sentenceTokens;
        }

        if (currentText.Length > 0)
        {
            var chunkText = contextPrefix + currentText.ToString().Trim();
            chunks.Add(CreateParagraphChunk(chunkText, sectionPath, page, chunkIndex++, hasOverlap));
        }

        return chunks;
    }

    /// <summary>
    /// Split text into sentences.
    /// </summary>
    private List<string> SplitIntoSentences(string text)
    {
        var sentences = Regex.Split(text, @"(?<=[.!?])\s+")
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToList();

        return sentences;
    }

    /// <summary>
    /// Split text by token count when sentence splitting isn't possible.
    /// </summary>
    private List<ChunkResult> SplitByCharacters(string text, string contextPrefix, string sectionPath, int? page, ref int chunkIndex)
    {
        var chunks = new List<ChunkResult>();
        var contextTokens = CountTokens(contextPrefix);
        var targetTokensPerChunk = TargetTokenMax - contextTokens;

        // Approximate chars per token for splitting
        var charsPerToken = 4.0;
        var targetChars = (int)(targetTokensPerChunk * charsPerToken);
        var overlapChars = (int)(OverlapTokens * charsPerToken);

        var position = 0;
        var hasOverlap = false;

        while (position < text.Length)
        {
            var remainingLength = text.Length - position;
            var chunkLength = Math.Min(targetChars, remainingLength);

            // Try to split at word boundary
            if (position + chunkLength < text.Length)
            {
                var searchStart = Math.Max(0, chunkLength - 50);
                var searchEnd = Math.Min(50, chunkLength - searchStart);
                var searchArea = text.Substring(position + searchStart, searchEnd);
                var lastSpace = searchArea.LastIndexOf(' ');
                if (lastSpace >= 0)
                {
                    chunkLength = searchStart + lastSpace;
                }
            }

            var chunkText = text.Substring(position, chunkLength).Trim();
            if (!string.IsNullOrEmpty(chunkText))
            {
                var textWithContext = contextPrefix + chunkText;
                chunks.Add(CreateParagraphChunk(textWithContext, sectionPath, page, chunkIndex++, hasOverlap));
            }

            position += chunkLength - overlapChars;
            if (position < 0) position = chunkLength;
            hasOverlap = true;
        }

        return chunks;
    }

    /// <summary>
    /// Create a chunk result for a paragraph.
    /// </summary>
    private ChunkResult CreateParagraphChunk(string text, string section, int? page, int index, bool hasOverlap)
    {
        return new ChunkResult
        {
            Index = index,
            Text = text.Trim(),
            SectionTitle = section,
            Page = page,
            EstimatedTokens = CountTokens(text),
            HasOverlap = hasOverlap
        };
    }

    /// <summary>
    /// Get overlap text from the end of a chunk.
    /// </summary>
    private string GetOverlapText(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        // Calculate target overlap size based on tokens
        var targetTokens = OverlapTokens;
        var charsPerToken = 4.0;
        var targetChars = (int)(targetTokens * charsPerToken);

        if (text.Length <= targetChars) return "";

        var lastPart = text[^targetChars..];

        // Try to start from sentence boundary
        var sentenceEnd = lastPart.IndexOfAny(new[] { '.', '!', '?' });
        if (sentenceEnd >= 0 && sentenceEnd < lastPart.Length - 10)
        {
            return lastPart[(sentenceEnd + 1)..].Trim();
        }

        // Start from word boundary
        var spaceIndex = lastPart.IndexOf(' ');
        if (spaceIndex >= 0)
        {
            return lastPart[(spaceIndex + 1)..].Trim();
        }

        return lastPart.Trim();
    }

    /// <summary>
    /// Merge chunks that are too short (but respect section boundaries).
    /// </summary>
    private List<ChunkResult> MergeShortChunks(List<ChunkResult> chunks)
    {
        if (chunks.Count <= 1) return chunks;

        var result = new List<ChunkResult>();

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var tokens = chunk.EstimatedTokens;

            // Only merge if same section and very short
            if (tokens < MinChunkTokens && result.Any() &&
                result[^1].SectionTitle == chunk.SectionTitle)
            {
                var prev = result[^1];
                result[^1] = prev with
                {
                    Text = prev.Text + "\n\n" + chunk.Text,
                    EstimatedTokens = prev.EstimatedTokens + tokens
                };
            }
            else
            {
                result.Add(chunk);
            }
        }

        // Renumber indices
        for (int i = 0; i < result.Count; i++)
        {
            result[i] = result[i] with { Index = i };
        }

        return result;
    }

    /// <summary>
    /// Helper class to build chunks incrementally with context injection.
    /// </summary>
    private class ChunkBuilder
    {
        private readonly StringBuilder _text = new();
        private readonly Func<string, int> _countTokens;
        private int? _page;
        private bool _hasOverlap;
        private bool _hasContext;

        public ChunkBuilder(Func<string, int> countTokens)
        {
            _countTokens = countTokens;
        }

        public bool HasContent => _text.Length > 0;
        public int TokenCount => _countTokens(_text.ToString());

        /// <summary>
        /// Append block with context injection (section prefix).
        /// </summary>
        public void AppendWithContext(StructuralBlock block, string contextPrefix, string sectionPath)
        {
            // Inject context at the beginning of the chunk
            if (!_hasContext && !string.IsNullOrEmpty(contextPrefix))
            {
                _text.Append(contextPrefix);
                _hasContext = true;
            }

            if (_text.Length > 0 && !_text.ToString().EndsWith("> "))
            {
                _text.AppendLine();
            }

            _text.Append(block.Text);
            _page ??= block.Page;
        }

        public void AppendOverlap(string overlapText)
        {
            if (!string.IsNullOrEmpty(overlapText))
            {
                _text.AppendLine(overlapText);
                _text.AppendLine();
                _hasOverlap = true;
            }
        }

        public ChunkResult Build(int index, string sectionTitle)
        {
            return new ChunkResult
            {
                Index = index,
                Text = _text.ToString().Trim(),
                SectionTitle = sectionTitle,
                Page = _page,
                EstimatedTokens = TokenCount,
                HasOverlap = _hasOverlap
            };
        }
    }
}
