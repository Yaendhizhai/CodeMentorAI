namespace CodeMentor.Application.GitHub;

public interface IGitHubRepositoryClient
{
    Task<GitHubRepositoryMetadata> GetPublicRepositoryAsync(
        GitHubRepositoryReference repository,
        CancellationToken cancellationToken = default);

    Task<GitHubRepositoryTree> GetRepositoryTreeAsync(
        GitHubRepositoryReference repository,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GitHubRepositoryFileContent>> GetRepositoryFilesAsync(
        GitHubRepositoryReference repository,
        GitHubRepositoryTree tree,
        IReadOnlyCollection<string> selectedPaths,
        CancellationToken cancellationToken = default);
}
