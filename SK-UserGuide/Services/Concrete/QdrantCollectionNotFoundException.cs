namespace SK_UserGuide.Services.Concrete;

public sealed class QdrantCollectionNotFoundException : Exception
{
    public QdrantCollectionNotFoundException(string collectionName)
        : base($"Tenant collection '{collectionName}' does not exist.")
    {
        CollectionName = collectionName;
    }

    public string CollectionName { get; }
}
