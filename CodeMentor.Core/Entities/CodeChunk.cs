namespace CodeMentor.Core.Entities;

public sealed class CodeChunk
{
    private CodeChunk()
    {
        Content = string.Empty;
    }

    public CodeChunk(Guid repositoryFileId, int chunkIndex, int startLine, int endLine, string content)
    {
        EntityValidation.RequiredId(repositoryFileId, nameof(repositoryFileId));
        if (chunkIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkIndex), "Chunk index cannot be negative.");
        }

        if (startLine < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(startLine), "Line numbers start at one.");
        }

        if (endLine < startLine)
        {
            throw new ArgumentOutOfRangeException(nameof(endLine), "End line must be greater than or equal to start line.");
        }

        Id = Guid.NewGuid();
        RepositoryFileId = repositoryFileId;
        ChunkIndex = chunkIndex;
        StartLine = startLine;
        EndLine = endLine;
        ArgumentException.ThrowIfNullOrEmpty(content);
        Content = content;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid RepositoryFileId { get; private set; }

    public int ChunkIndex { get; private set; }

    public int StartLine { get; private set; }

    public int EndLine { get; private set; }

    public string Content { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public RepositoryFile RepositoryFile { get; private set; } = null!;
}
