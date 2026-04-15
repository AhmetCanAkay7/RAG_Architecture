using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using SK_UserGuide.Services.Abstract;

namespace SK_UserGuide.Services.Ingestion;

/// <summary>
/// Orchestrates the complete document ingestion pipeline:
/// Extract → Clean → Chunk → Embed → Store
/// Supports multi-tenant isolation via tenantId (mapped to Qdrant collection).
/// </summary>
public class DocumentIngestionOrchestrator : IDocumentIngestionOrchestrator
{
    private readonly PdfTextExtractor _pdfExtractor;
    private readonly TxtTextExtractor _txtExtractor;
    private readonly DocxTextExtractor _docxExtractor;
    private readonly StructuralChunker _chunker;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly IQdrantIngestionRepository _qdrantRepo;
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
        DocumentMetadataBuilder metadataBuilder,
        ILogger<DocumentIngestionOrchestrator> logger)
    {
        _pdfExtractor = pdfExtractor;
        _txtExtractor = txtExtractor;
        _docxExtractor = docxExtractor;
        _chunker = chunker;
        _embeddingGenerator = embeddingGenerator;
        _qdrantRepo = qdrantRepo;
        _metadataBuilder = metadataBuilder;
        _logger = logger;
    }

    /// <summary>
    /// Ingest a document file into the vector store for a specific tenant.
    /// </summary>
    /// <param name="file">The uploaded document file.</param>
    /// <param name="tenantId">Tenant identifier (used as Qdrant collection name).</param>
    public async Task<IngestionResult> IngestAsync(IFormFile file, string tenantId)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 1. File informations
            var fileName = file.FileName;
            var extension = Path.GetExtension(fileName).ToLower();
            var sourceType = extension switch
            {
                ".pdf" => "pdf",
                ".docx" => "docx",
                _ => "txt"
            };
            var docId = _metadataBuilder.GenerateStableDocId(fileName);

            _logger.LogInformation(
                "Starting ingestion for {FileName}, DocId: {DocId}, Tenant: {TenantId}",
                fileName, docId, tenantId);

            // 2. Text extraction
            ExtractedDocument extractedDoc;
            using (var stream = file.OpenReadStream())
            {
                extractedDoc = extension switch
                {
                    ".pdf" => _pdfExtractor.Extract(stream, fileName),
                    ".docx" => _docxExtractor.Extract(stream, fileName),
                    ".txt" => _txtExtractor.Extract(stream, fileName),
                    _ => throw new NotSupportedException(
                        $"Unsupported file format: {extension}. Supported formats: .pdf, .docx, .txt")
                };
            }

            _logger.LogInformation("Extracted {PageCount} pages from {FileName}",
                extractedDoc.Pages.Count, fileName);

            // 3. Chunking
            var chunks = _chunker.Chunk(extractedDoc);

            if (chunks.Count == 0)
            {
                return new IngestionResult
                {
                    Success = false,
                    Message = "Document could not be processed: No valid content found."
                };
            }

            _logger.LogInformation("Created {ChunkCount} chunks from {FileName}", chunks.Count, fileName);

            // 4. Ensure collection exists and clear existing chunks
            await _qdrantRepo.EnsureCollectionAsync(tenantId);
            await _qdrantRepo.DeleteByDocIdAsync(tenantId, docId);
            _logger.LogInformation("Cleared existing chunks for DocId: {DocId} in collection: {TenantId}", docId, tenantId);

            // 5. Embedding generation
            var texts = chunks.Select(c => c.Text).ToList();
            var embeddings = await _embeddingGenerator.GenerateAsync(texts);
            var vectors = embeddings.Select(e => e.Vector.ToArray()).ToList();

            _logger.LogInformation("Generated {EmbeddingCount} embeddings", vectors.Count);

            // 6. Store in Qdrant
            await _qdrantRepo.UpsertChunksAsync(
                tenantId,
                docId,
                fileName,
                sourceType,
                chunks,
                vectors);

            _logger.LogInformation("Upserted {ChunkCount} chunks to Qdrant collection: {TenantId}", chunks.Count, tenantId);

            stopwatch.Stop();

            // Chunk statistics
            var avgTokens = chunks.Average(c => c.EstimatedTokens);
            var minTokens = chunks.Min(c => c.EstimatedTokens);
            var maxTokens = chunks.Max(c => c.EstimatedTokens);

            _logger.LogInformation(
                "Ingestion complete for {FileName}: {ChunkCount} chunks, avg {AvgTokens:F0} tokens, " +
                "range [{MinTokens}-{MaxTokens}], {ElapsedMs}ms, tenant: {TenantId}",
                fileName, chunks.Count, avgTokens, minTokens, maxTokens, stopwatch.ElapsedMilliseconds, tenantId);

            return new IngestionResult
            {
                Success = true,
                DocId = docId,
                ChunkCount = chunks.Count,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                Message = $"Successfully processed: {chunks.Count} chunks, avg {avgTokens:F0} tokens/chunk"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ingestion failed for {FileName} in tenant {TenantId}", file.FileName, tenantId);

            return new IngestionResult
            {
                Success = false,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                Message = $"Error: {ex.Message}"
            };
        }
    }
}
