namespace SK_UserGuide.Services.Abstract;

/// <summary>
/// Interface for hybrid retrieval service combining dense and sparse search.
/// </summary>
public interface IHybridRetrievalService
{
    /// <summary>
    /// Perform hybrid retrieval: dense + sparse search with smart selection.
    /// </summary>
    Task<SK_UserGuide.Services.Retrieval.RetrievalResult> RetrieveAsync(string question);
}
