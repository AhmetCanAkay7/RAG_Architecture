namespace SK_UserGuide.Services.Abstract;

/// <summary>
/// Interface for document version management in Qdrant.
/// </summary>
public interface IVersionManager
{
    Task<int> GetCurrentVersionAsync(string docId);
    Task CleanOldVersionsAsync(string docId, int keepVersions = 2);
}
