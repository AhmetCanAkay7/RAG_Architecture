using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using SK_UserGuide.Services.Abstract;

namespace SK_UserGuide.Services.Ingestion;

/// <summary>
/// Orchestrates the complete document ingestion pipeline:
/// Extract → Clean → Chunk → Embed → Store
/// </summary>
public class DocumentIngestionOrchestrator : IDocumentIngestionOrchestrator
{
    private readonly PdfTextExtractor _pdfExtractor;
    private readonly TxtTextExtractor _txtExtractor;
    private readonly DocxTextExtractor _docxExtractor;
    private readonly TextCleaner _textCleaner;
    private readonly StructuralChunker _chunker;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly IQdrantIngestionRepository _qdrantRepo;
    private readonly IVersionManager _versionManager;
    private readonly DocumentMetadataBuilder _metadataBuilder;
    private readonly ILogger<DocumentIngestionOrchestrator> _logger;

    public DocumentIngestionOrchestrator(
        PdfTextExtractor pdfExtractor,
        TxtTextExtractor txtExtractor,
        DocxTextExtractor docxExtractor,
        TextCleaner textCleaner,
        StructuralChunker chunker,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IQdrantIngestionRepository qdrantRepo,
        IVersionManager versionManager,
        DocumentMetadataBuilder metadataBuilder,
        ILogger<DocumentIngestionOrchestrator> logger)
    {
        _pdfExtractor = pdfExtractor;
        _txtExtractor = txtExtractor;
        _docxExtractor = docxExtractor;
        _textCleaner = textCleaner;
        _chunker = chunker;
        _embeddingGenerator = embeddingGenerator;
        _qdrantRepo = qdrantRepo;
        _versionManager = versionManager;
        _metadataBuilder = metadataBuilder;
        _logger = logger;
    }

    /// <summary>
    /// Ingest a document file into the vector store.
    /// </summary>
    public async Task<IngestionResult> IngestAsync(IFormFile file)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 1. Dosya bilgileri
            var fileName = file.FileName;
            var extension = Path.GetExtension(fileName).ToLower();
            var sourceType = extension switch
            {
                ".pdf" => "pdf",
                ".docx" => "docx",
                _ => "txt"
            };
            var docId = _metadataBuilder.GenerateStableDocId(fileName);

            _logger.LogInformation("Starting ingestion for {FileName}, DocId: {DocId}", fileName, docId);

            // 2. Metin çıkarma
            ExtractedDocument extractedDoc;
            using (var stream = file.OpenReadStream())
            {
                extractedDoc = extension switch
                {
                    ".pdf" => _pdfExtractor.Extract(stream, fileName),
                    ".docx" => _docxExtractor.Extract(stream, fileName),
                    ".txt" => _txtExtractor.Extract(stream, fileName),
                    _ => throw new NotSupportedException(
                        $"Desteklenmeyen dosya formatı: {extension}. Desteklenen formatlar: .pdf, .docx, .txt")
                };
            }

            _logger.LogInformation("Extracted {PageCount} pages from {FileName}",
                extractedDoc.Pages.Count, fileName);

            // 3. Chunking (temizleme chunker içinde yapılıyor)
            var chunks = _chunker.Chunk(extractedDoc);

            if (chunks.Count == 0)
            {
                return new IngestionResult
                {
                    Success = false,
                    Message = "Doküman işlenemedi: Geçerli içerik bulunamadı."
                };
            }

            _logger.LogInformation("Created {ChunkCount} chunks from {FileName}", chunks.Count, fileName);

            // 4. Versiyon belirleme
            var currentVersion = await _versionManager.GetCurrentVersionAsync(docId);
            var newVersion = currentVersion + 1;

            _logger.LogInformation("Document {DocId} version: {CurrentVersion} -> {NewVersion}",
                docId, currentVersion, newVersion);

            // 5. Embedding üretimi
            var texts = chunks.Select(c => c.Text).ToList();
            var embeddings = await _embeddingGenerator.GenerateAsync(texts);
            var vectors = embeddings.Select(e => e.Vector.ToArray()).ToList();

            _logger.LogInformation("Generated {EmbeddingCount} embeddings", vectors.Count);

            // 6. Qdrant'a kaydet
            await _qdrantRepo.UpsertChunksAsync(
                docId,
                fileName,
                sourceType,
                newVersion,
                chunks,
                vectors);

            _logger.LogInformation("Upserted {ChunkCount} chunks to Qdrant", chunks.Count);

            // 7. Eski versiyonları temizle (son 2 versiyonu tut)
            await _versionManager.CleanOldVersionsAsync(docId, keepVersions: 2);

            stopwatch.Stop();

            // Chunk istatistikleri
            var avgTokens = chunks.Average(c => c.EstimatedTokens);
            var minTokens = chunks.Min(c => c.EstimatedTokens);
            var maxTokens = chunks.Max(c => c.EstimatedTokens);

            _logger.LogInformation(
                "Ingestion complete for {FileName}: {ChunkCount} chunks, avg {AvgTokens:F0} tokens, " +
                "range [{MinTokens}-{MaxTokens}], version {Version}, {ElapsedMs}ms",
                fileName, chunks.Count, avgTokens, minTokens, maxTokens, newVersion, stopwatch.ElapsedMilliseconds);

            return new IngestionResult
            {
                Success = true,
                DocId = docId,
                Version = newVersion,
                ChunkCount = chunks.Count,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                Message = $"Başarıyla işlendi: {chunks.Count} chunk, versiyon {newVersion}, " +
                          $"ortalama {avgTokens:F0} token/chunk"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ingestion failed for {FileName}", file.FileName);

            return new IngestionResult
            {
                Success = false,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                Message = $"Hata: {ex.Message}"
            };
        }
    }
}
