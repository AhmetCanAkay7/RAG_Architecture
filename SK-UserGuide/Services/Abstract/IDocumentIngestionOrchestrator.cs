using Microsoft.AspNetCore.Http;

namespace SK_UserGuide.Services.Abstract;

/// <summary>
/// Interface for document ingestion orchestration.
/// </summary>
public interface IDocumentIngestionOrchestrator
{
    /// <summary>
    /// Ingest a document file into the vector store.
    /// </summary>
    Task<SK_UserGuide.Services.Ingestion.IngestionResult> IngestAsync(IFormFile file);
}
