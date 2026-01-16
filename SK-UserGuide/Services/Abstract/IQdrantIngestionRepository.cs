using SK_UserGuide.Services.Ingestion;

namespace SK_UserGuide.Services.Abstract;
public interface IQdrantIngestionRepository
{
    /// <summary>
    /// Ensure collection exists with proper configuration.
    /// </summary>
    Task EnsureCollectionAsync();
    Task UpsertChunksAsync(
        string docId,
        string docName,
        string sourceType,
        int version,
        List<ChunkResult> chunks,
        List<float[]> vectors);
    Task<int> DeleteByDocIdAsync(string docId);
    Task<int> DeleteByDocIdAndVersionAsync(string docId, int version);
    Task<List<DocumentInfo>> GetAllDocumentsAsync();
}
