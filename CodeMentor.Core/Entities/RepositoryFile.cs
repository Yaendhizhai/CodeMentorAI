namespace CodeMentor.Core.Entities;

public sealed class RepositoryFile
{
    private RepositoryFile()
    {
        RelativePath = string.Empty;
    }

    public RepositoryFile(
        Guid projectId,
        string relativePath,
        string? language = null,
        string? contentHash = null,
        long sizeInBytes = 0)
    {
        EntityValidation.RequiredId(projectId, nameof(projectId));
        if (sizeInBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeInBytes), "File size cannot be negative.");
        }

        Id = Guid.NewGuid();
        ProjectId = projectId;
        RelativePath = EntityValidation.RelativePath(relativePath, nameof(relativePath), 1024);
        Language = EntityValidation.OptionalText(language, nameof(language), 100);
        ContentHash = EntityValidation.OptionalText(contentHash, nameof(contentHash), 128);
        SizeInBytes = sizeInBytes;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public string RelativePath { get; private set; }

    public string? Language { get; private set; }

    /// <summary>
    /// Source object identifier; for GitHub ingestion this is the Git blob SHA, not necessarily a SHA-256 digest.
    /// </summary>
    public string? ContentHash { get; private set; }

    public long SizeInBytes { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public RepositoryProject Project { get; private set; } = null!;

    public ICollection<CodeChunk> Chunks { get; private set; } = new List<CodeChunk>();
}
