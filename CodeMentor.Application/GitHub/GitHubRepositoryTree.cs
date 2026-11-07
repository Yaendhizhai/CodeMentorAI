namespace CodeMentor.Application.GitHub;

public enum GitHubRepositoryTreeEntryType
{
    Blob,
    Tree,
    Commit
}

public sealed record GitHubRepositoryTreeEntry(
    string Path,
    GitHubRepositoryTreeEntryType Type,
    long? Size,
    string Sha);

public sealed record GitHubRepositoryTree(
    string Owner,
    string RepositoryName,
    string Sha,
    IReadOnlyList<GitHubRepositoryTreeEntry> Entries);

public sealed record GitHubRepositoryTreeLimits
{
    public const int DefaultMaximumResponseBytes = 10 * 1024 * 1024;
    public const int DefaultMaximumEntries = 50_000;

    public GitHubRepositoryTreeLimits(
        int maximumResponseBytes = DefaultMaximumResponseBytes,
        int maximumEntries = DefaultMaximumEntries)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumResponseBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntries);
        MaximumResponseBytes = maximumResponseBytes;
        MaximumEntries = maximumEntries;
    }

    public int MaximumResponseBytes { get; }

    public int MaximumEntries { get; }
}
