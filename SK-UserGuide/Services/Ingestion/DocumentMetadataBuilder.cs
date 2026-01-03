using System.Security.Cryptography;
using System.Text;

namespace SK_UserGuide.Services.Ingestion;

/// <summary>
/// Builds standardized metadata for document chunks.
/// Handles stable document IDs, versioning, and content hashing.
/// </summary>
public class DocumentMetadataBuilder
{
    /// <summary>
    /// Generate a stable document ID from filename.
    /// Same file uploaded multiple times gets the same ID.
    /// </summary>
    public string GenerateStableDocId(string fileName)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(fileName.ToLowerInvariant().Trim()));
        return Convert.ToHexString(hash)[..16]; // İlk 16 karakter
    }

    /// <summary>
    /// Generate content hash for deduplication.
    /// </summary>
    public string GenerateContentHash(string text)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash)[..32]; // İlk 32 karakter
    }

    /// <summary>
    /// Generate a unique point ID for Qdrant.
    /// </summary>
    public ulong GeneratePointId(string docId, int version, int chunkIndex)
    {
        var combined = $"{docId}:{version}:{chunkIndex}";
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(combined));
        return BitConverter.ToUInt64(hash, 0);
    }

    /// <summary>
    /// Build complete payload for a chunk.
    /// </summary>
    public ChunkPayload BuildPayload(
        string docId,
        string docName,
        string sourceType,
        int version,
        ChunkResult chunk)
    {
        return new ChunkPayload
        {
            DocId = docId,
            DocName = docName,
            SourceType = sourceType,
            Version = version,
            CreatedAt = DateTime.UtcNow,
            ChunkIndex = chunk.Index,
            Text = chunk.Text,
            Language = "en",
            ContentHash = GenerateContentHash(chunk.Text),
            Page = chunk.Page,
            SectionTitle = string.IsNullOrEmpty(chunk.SectionTitle) ? null : chunk.SectionTitle,
            EstimatedTokens = chunk.EstimatedTokens,
            HasOverlap = chunk.HasOverlap
        };
    }
}
