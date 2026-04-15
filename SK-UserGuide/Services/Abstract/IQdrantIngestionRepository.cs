using SK_UserGuide.Services.Ingestion;

namespace SK_UserGuide.Services.Abstract;

/// <summary>
/// Repository interface for Qdrant ingestion operations.
/// All operations are collection-aware for multi-tenant support.
/// </summary>
public interface IQdrantIngestionRepository
{
    /// <summary>
    /// Ensure collection exists with proper configuration.
    /// </summary>
    /// <param name="collectionName">Target Qdrant collection name.</param>
    Task EnsureCollectionAsync(string collectionName);

    /// <summary>
    /// Upsert document chunks with embeddings into a collection.
    /// </summary>
    Task UpsertChunksAsync(
        string collectionName,
        string docId,
        string docName,
        string sourceType,
        List<ChunkResult> chunks,
        List<float[]> vectors);

    /// <summary>
    /// Delete all points for a document by doc_id.
    /// </summary>
    Task<int> DeleteByDocIdAsync(string collectionName, string docId);

    /// <summary>
    /// Get list of all documents in a collection.
    /// </summary>
    Task<List<DocumentInfo>> GetAllDocumentsAsync(string collectionName);
}
