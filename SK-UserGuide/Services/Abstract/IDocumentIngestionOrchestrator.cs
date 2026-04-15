using Microsoft.AspNetCore.Http;
using SK_UserGuide.Services.Ingestion;

namespace SK_UserGuide.Services.Abstract;

/// <summary>
/// Interface for document ingestion orchestration.
/// </summary>
public interface IDocumentIngestionOrchestrator
{
    /// <summary>
    /// Ingest a document file into the vector store for a specific tenant.
    /// </summary>
    /// <param name="file">The uploaded document file.</param>
    /// <param name="tenantId">Tenant identifier (maps to Qdrant collection name).</param>
    Task<IngestionResult> IngestAsync(IFormFile file, string tenantId);
}
