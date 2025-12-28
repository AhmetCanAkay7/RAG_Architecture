using System.Text;
using System.Text.RegularExpressions;

namespace SK_UserGuide.Services.Ingestion;

/// <summary>
/// Structural chunking algorithm optimized for Turkish user guides.
/// Features:
/// - Heading/paragraph/step list detection
/// - Token-based budgeting (400-700 tokens target)
/// - Overlap support (100 tokens)
/// - Step list preservation (adım listeleri bölünmez)
/// </summary>
public class StructuralChunker
{
    private const int TargetTokenMin = 400;
    private const int TargetTokenMax = 700;
    private const int OverlapTokens = 100;
    private const int MinChunkTokens = 100;
    private const double CharsPerToken = 3.5; // Türkçe için ortalama

    private readonly TextCleaner _textCleaner;

    public StructuralChunker(TextCleaner textCleaner)
    {
        _textCleaner = textCleaner;
    }

    /// <summary>
    /// Chunk a document into semantically meaningful pieces.
    /// </summary>
    public List<ChunkResult> Chunk(ExtractedDocument document)
    {
        var chunks = new List<ChunkResult>();

        // Tüm sayfaları birleştir ve temizle
        var cleanedPages = document.Pages
            .Select(p => new PageContent(p.PageNumber, _textCleaner.Clean(p.Text)))
            .ToList();
        var cleanedDoc = document with { Pages = cleanedPages };

        // Yapısal blokları parse et
        var blocks = ParseStructuralBlocks(cleanedDoc);

        var currentChunk = new ChunkBuilder();
        string currentSection = "";
        int chunkIndex = 0;

        foreach (var block in blocks)
        {
            // Boş blokları atla
            if (block.Type == BlockType.Empty || string.IsNullOrWhiteSpace(block.Text))
                continue;

            // Başlık bloğu - yeni section başlat
            if (block.Type == BlockType.Heading)
            {
                if (currentChunk.HasContent)
                {
                    chunks.Add(currentChunk.Build(chunkIndex++, currentSection));
                    currentChunk = new ChunkBuilder();
                }
                currentSection = block.Text.Trim();
                currentChunk.Append(block);
                continue;
            }

            // Adım listesi - mümkün olduğunca bölünmemeli
            if (block.Type == BlockType.StepList)
            {
                // Mevcut chunk'ı önce kaydet
                if (currentChunk.HasContent)
                {
                    chunks.Add(currentChunk.Build(chunkIndex++, currentSection));
                    currentChunk = new ChunkBuilder();
                }

                var stepChunks = ChunkStepList(block, currentSection, ref chunkIndex);
                chunks.AddRange(stepChunks);
                continue;
            }

            // Normal paragraf - token budgeting uygula
            var projectedTokens = currentChunk.EstimatedTokens + EstimateTokens(block.Text);

            if (projectedTokens > TargetTokenMax && currentChunk.HasContent)
            {
                // Chunk'ı kaydet
                chunks.Add(currentChunk.Build(chunkIndex++, currentSection));

                // Yeni chunk başlat - overlap ile
                currentChunk = new ChunkBuilder();
                if (chunks.Count > 0)
                {
                    var overlapText = GetOverlapText(chunks.Last().Text);
                    if (!string.IsNullOrEmpty(overlapText))
                    {
                        currentChunk.AppendOverlap(overlapText);
                    }
                }
            }

            currentChunk.Append(block);
        }

        // Son chunk'ı kaydet
        if (currentChunk.HasContent)
        {
            chunks.Add(currentChunk.Build(chunkIndex, currentSection));
        }

        // Çok kısa chunk'ları birleştir
        chunks = MergeShortChunks(chunks);

        return chunks;
    }

    /// <summary>
    /// Parse document into structural blocks (headings, paragraphs, step lists).
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

                // Tip değiştiğinde veya yeni başlık/liste başladığında bloğu kaydet
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
        // Başlık her zaman yeni blok başlatır
        if (newType == BlockType.Heading) return true;

        // Liste tipinden paragraf tipine geçiş
        if (currentType == BlockType.StepList && newType == BlockType.Paragraph) return true;

        // Paragraftan listeye geçiş
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

        // Markdown başlıkları
        if (trimmed.StartsWith('#')) return BlockType.Heading;

        // Numaralı başlıklar: "1. Giriş", "2.1 Alt Başlık"
        if (Regex.IsMatch(trimmed, @"^(\d+\.)+\s+[A-ZÇĞİÖŞÜ][a-zçğıöşü]") && trimmed.Length < 80)
            return BlockType.Heading;

        // Tamamen büyük harf başlıklar (PDF'lerde yaygın)
        if (trimmed.Length > 3 && trimmed.Length < 60 &&
            trimmed.All(c => char.IsUpper(c) || char.IsWhiteSpace(c) || char.IsDigit(c) || "ÇĞİÖŞÜ.-:".Contains(c)))
            return BlockType.Heading;

        // Adım/madde listesi pattern'leri
        var stepPatterns = new[]
        {
            @"^Adım\s*\d+[:\.\s]",           // "Adım 1:", "Adım 2."
            @"^\d+[\.\)]\s+\S",               // "1. ...", "1) ..."
            @"^[a-zçğıöşü][\.\)]\s+\S",       // "a. ...", "a) ..."
            @"^[•\-\*]\s+\S",                 // "• ...", "- ...", "* ..."
            @"^➤\s+\S",                       // "➤ ..."
            @"^Step\s*\d+[:\.\s]"             // "Step 1:" (İngilizce support)
        };

        if (stepPatterns.Any(p => Regex.IsMatch(trimmed, p, RegexOptions.IgnoreCase)))
            return BlockType.StepList;

        return BlockType.Paragraph;
    }

    /// <summary>
    /// Chunk a step list while trying to keep related steps together.
    /// </summary>
    private List<ChunkResult> ChunkStepList(StructuralBlock block, string section, ref int chunkIndex)
    {
        var chunks = new List<ChunkResult>();
        var steps = ParseIndividualSteps(block.Text);

        if (steps.Count == 0)
        {
            // Adım parse edilemezse tek chunk olarak döndür
            var tokens = EstimateTokens(block.Text);
            chunks.Add(new ChunkResult
            {
                Index = chunkIndex++,
                Text = block.Text,
                SectionTitle = section,
                Page = block.Page,
                EstimatedTokens = tokens,
                HasOverlap = false
            });
            return chunks;
        }

        var currentSteps = new List<string>();
        int currentTokens = 0;

        foreach (var step in steps)
        {
            var stepTokens = EstimateTokens(step);

            // Tek bir adım bile çok uzunsa, onu kendi başına chunk yap
            if (stepTokens > TargetTokenMax)
            {
                if (currentSteps.Any())
                {
                    chunks.Add(CreateStepChunk(currentSteps, section, chunkIndex++, block.Page));
                    currentSteps.Clear();
                    currentTokens = 0;
                }
                chunks.Add(new ChunkResult
                {
                    Index = chunkIndex++,
                    Text = step,
                    SectionTitle = section,
                    Page = block.Page,
                    EstimatedTokens = stepTokens,
                    HasOverlap = false
                });
                continue;
            }

            // Token limiti aşılıyorsa yeni chunk
            if (currentTokens + stepTokens > TargetTokenMax && currentSteps.Any())
            {
                chunks.Add(CreateStepChunk(currentSteps, section, chunkIndex++, block.Page));
                currentSteps.Clear();
                currentTokens = 0;
            }

            currentSteps.Add(step);
            currentTokens += stepTokens;
        }

        if (currentSteps.Any())
        {
            chunks.Add(CreateStepChunk(currentSteps, section, chunkIndex++, block.Page));
        }

        return chunks;
    }

    /// <summary>
    /// Parse step list text into individual steps.
    /// </summary>
    private List<string> ParseIndividualSteps(string text)
    {
        // Her adımı ayır - çeşitli pattern'lere göre
        var stepPattern = @"(?=(?:Adım\s*\d+[:\.\s]|\d+[\.\)]\s|[•\-\*➤]\s))";
        var parts = Regex.Split(text, stepPattern, RegexOptions.IgnoreCase)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToList();

        return parts.Any() ? parts : new List<string> { text };
    }

    /// <summary>
    /// Create a chunk from a list of steps.
    /// </summary>
    private ChunkResult CreateStepChunk(IEnumerable<string> steps, string section, int index, int? page)
    {
        var text = string.Join("\n", steps);
        return new ChunkResult
        {
            Index = index,
            Text = text,
            SectionTitle = section,
            Page = page,
            EstimatedTokens = EstimateTokens(text),
            HasOverlap = false
        };
    }

    /// <summary>
    /// Estimate token count for text (Turkish: ~3.5 chars/token).
    /// </summary>
    private int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return (int)(text.Length / CharsPerToken);
    }

    /// <summary>
    /// Get overlap text from the end of a chunk.
    /// </summary>
    private string GetOverlapText(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var targetChars = (int)(OverlapTokens * CharsPerToken);
        if (text.Length <= targetChars) return "";

        // Son kısmı al
        var lastPart = text[^targetChars..];

        // Cümle başından başlamaya çalış
        var sentenceEnd = lastPart.IndexOfAny(new[] { '.', '!', '?' });
        if (sentenceEnd >= 0 && sentenceEnd < lastPart.Length - 10)
        {
            return lastPart[(sentenceEnd + 1)..].Trim();
        }

        // Kelime sınırından başla
        var spaceIndex = lastPart.IndexOf(' ');
        if (spaceIndex >= 0)
        {
            return lastPart[(spaceIndex + 1)..].Trim();
        }

        return lastPart.Trim();
    }

    /// <summary>
    /// Merge chunks that are too short.
    /// </summary>
    private List<ChunkResult> MergeShortChunks(List<ChunkResult> chunks)
    {
        if (chunks.Count <= 1) return chunks;

        var result = new List<ChunkResult>();

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var tokens = chunk.EstimatedTokens;

            if (tokens < MinChunkTokens && result.Any())
            {
                // Önceki chunk'a ekle
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

        // Index'leri yeniden numarala
        for (int i = 0; i < result.Count; i++)
        {
            result[i] = result[i] with { Index = i };
        }

        return result;
    }

    /// <summary>
    /// Helper class to build chunks incrementally.
    /// </summary>
    private class ChunkBuilder
    {
        private readonly StringBuilder _text = new();
        private int? _page;
        private bool _hasOverlap;

        public bool HasContent => _text.Length > 0;
        public int EstimatedTokens => (int)(_text.Length / CharsPerToken);

        public void Append(StructuralBlock block)
        {
            if (_text.Length > 0) _text.AppendLine();
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
                EstimatedTokens = EstimatedTokens,
                HasOverlap = _hasOverlap
            };
        }
    }
}
