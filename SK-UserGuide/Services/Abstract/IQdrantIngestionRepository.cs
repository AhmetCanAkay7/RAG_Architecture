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
        List<ChunkResult> chunks,
        List<float[]> vectors);
    Task<int> DeleteByDocIdAsync(string docId);
    Task<List<DocumentInfo>> GetAllDocumentsAsync();
}
