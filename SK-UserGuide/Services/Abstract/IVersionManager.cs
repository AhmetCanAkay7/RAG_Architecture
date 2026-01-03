namespace SK_UserGuide.Services.Abstract;

/// <summary>
/// Interface for document version management in Qdrant.
/// </summary>
public interface IVersionManager
{
    /// <summary>
    /// Get the current (highest) version for a document.
    /// </summary>
    Task<int> GetCurrentVersionAsync(string docId);

    /// <summary>
    /// Delete old versions of a document, keeping the specified number of recent versions.
    /// </summary>
    Task CleanOldVersionsAsync(string docId, int keepVersions = 2);
}
