namespace SK_UserGuide.Services.Abstract;

/// <summary>
/// Interface for Qdrant ingestion repository operations.
/// </summary>
public interface IQdrantIngestionRepository
{
    /// <summary>
    /// Ensure collection exists with proper configuration.
    /// </summary>
    Task EnsureCollectionAsync();

    /// <summary>
    /// Upsert chunks with their embeddings and metadata.
    /// </summary>
    Task UpsertChunksAsync(
        string docId,
        string docName,
        string sourceType,
        int version,
        List<SK_UserGuide.Services.Ingestion.ChunkResult> chunks,
        List<float[]> vectors);

    /// <summary>
    /// Delete all points for a document by doc_id.
    /// </summary>
    Task<int> DeleteByDocIdAsync(string docId);

    /// <summary>
    /// Delete points for a specific document version.
    /// </summary>
    Task<int> DeleteByDocIdAndVersionAsync(string docId, int version);

    /// <summary>
    /// Get list of all documents in the collection with their versions.
    /// </summary>
    Task<List<SK_UserGuide.Services.Ingestion.DocumentInfo>> GetAllDocumentsAsync();
}
