using Microsoft.AspNetCore.Http;
using SK_UserGuide.Services.Ingestion;

namespace SK_UserGuide.Services.Abstract;

/// <summary>
/// Interface for document ingestion orchestration.
/// </summary>
public interface IDocumentIngestionOrchestrator
{
    /// <summary>
    /// Ingest a document file into the vector store.
    /// </summary>
    Task<IngestionResult> IngestAsync(IFormFile file);
}
