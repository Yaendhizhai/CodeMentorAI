namespace CodeMentor.Application.GitHub;

public sealed record GitHubRepositoryMetadata(
    string Owner,
    string Name,
    string CanonicalHtmlUrl,
    string DefaultBranch,
    string Visibility);
