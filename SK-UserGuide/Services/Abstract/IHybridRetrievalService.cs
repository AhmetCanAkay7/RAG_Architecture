using SK_UserGuide.Services.Retrieval;

namespace SK_UserGuide.Services.Abstract;

/// <summary>
/// Interface for hybrid retrieval service combining dense and sparse search.
/// </summary>
public interface IHybridRetrievalService
{
    /// <summary>
    /// Perform hybrid retrieval: dense + sparse search with smart selection.
    /// </summary>
    /// <param name="question">User question to search for.</param>
    /// <param name="collectionName">Qdrant collection name (tenant-specific).</param>
    Task<RetrievalResult> RetrieveAsync(string question, string collectionName);
}
