using CodeMentor.Application.GitHub;

namespace CodeMentor.Tests.GitHub;

public sealed class GitHubRepositoryReferenceTests
{
    [Theory]
    [InlineData("https://github.com/octo-org/project", "octo-org", "project")]
    [InlineData("https://GitHub.com/octo-org/project", "octo-org", "project")]
    [InlineData("https://github.com/octo-org/project.git", "octo-org", "project")]
    [InlineData("https://github.com/octo-org/project.GIT", "octo-org", "project")]
    [InlineData("https://github.com/octo-org/project_name.v2", "octo-org", "project_name.v2")]
    [InlineData("https://github.com/a/project", "a", "project")]
    public void TryParse_AcceptsPublicGitHubRepositoryUrls(
        string value,
        string expectedOwner,
        string expectedRepositoryName)
    {
        var parsed = GitHubRepositoryReference.TryParse(value, out var reference);

        Assert.True(parsed);
        Assert.NotNull(reference);
        Assert.Equal(expectedOwner, reference.Owner);
        Assert.Equal(expectedRepositoryName, reference.RepositoryName);
        Assert.Equal(
            $"https://github.com/{expectedOwner}/{expectedRepositoryName}",
            reference.CanonicalUrl);
    }

    [Theory]
    [InlineData("http://github.com/octo-org/project")]
    [InlineData("https://api.github.com/octo-org/project")]
    [InlineData("https://github.com.example.com/octo-org/project")]
    [InlineData("https://user@github.com/octo-org/project")]
    [InlineData("https://github.com:444/octo-org/project")]
    [InlineData("https://github.com/octo-org")]
    [InlineData("https://github.com/octo-org/")]
    [InlineData("https://github.com//project")]
    [InlineData("https://github.com/octo-org/project/issues")]
    [InlineData("https://github.com/octo-org/project/")]
    [InlineData("https://github.com/octo-org/project?tab=readme")]
    [InlineData("https://github.com/octo-org/project?")]
    [InlineData("https://github.com/octo-org/project#readme")]
    [InlineData("https://github.com/octo-org/project#")]
    [InlineData("https://github.com/octo-org%2Fother/project")]
    [InlineData("https://github.com/octo-org/project%2Fissues")]
    [InlineData("https://github.com/-octo/project")]
    [InlineData("https://github.com/octo-org/bad name")]
    [InlineData("not a URL")]
    [InlineData(" https://github.com/octo-org/project")]
    [InlineData("https://github.com/octo-org/project ")]
    public void TryParse_RejectsInvalidRepositoryUrls(string value)
    {
        var parsed = GitHubRepositoryReference.TryParse(value, out var reference);

        Assert.False(parsed);
        Assert.Null(reference);
    }

    [Fact]
    public void TryParse_RejectsNullOrWhitespace()
    {
        Assert.False(GitHubRepositoryReference.TryParse(null, out var nullReference));
        Assert.Null(nullReference);

        Assert.False(GitHubRepositoryReference.TryParse(" \t", out var whitespaceReference));
        Assert.Null(whitespaceReference);
    }

    [Fact]
    public void TryParse_AllowsGitSuffixBeyondNormalizedRepositoryNameLimit()
    {
        var repositoryName = new string('a', 100);
        var value = $"https://github.com/owner/{repositoryName}.git";

        var parsed = GitHubRepositoryReference.TryParse(value, out var reference);

        Assert.True(parsed);
        Assert.NotNull(reference);
        Assert.Equal(repositoryName, reference.RepositoryName);
    }

    [Theory]
    [InlineData("https://github.com/owner-name-that-is-too-long-for-a-valid-github-owner-name/valid")]
    [InlineData("https://github.com/owner/abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz")]
    public void TryParse_RejectsValuesOutsideGitHubIdentifierLimits(string value)
    {
        var parsed = GitHubRepositoryReference.TryParse(value, out var reference);

        Assert.False(parsed);
        Assert.Null(reference);
    }
}
