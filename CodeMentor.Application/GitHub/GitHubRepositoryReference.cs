namespace CodeMentor.Application.GitHub;

public sealed record GitHubRepositoryReference
{
    private GitHubRepositoryReference(string owner, string repositoryName)
    {
        Owner = owner;
        RepositoryName = repositoryName;
        CanonicalUrl = $"https://github.com/{owner}/{repositoryName}";
    }

    public string Owner { get; }

    public string RepositoryName { get; }

    public string CanonicalUrl { get; }

    public static bool TryParse(string? value, out GitHubRepositoryReference? reference)
    {
        reference = null;

        if (string.IsNullOrWhiteSpace(value) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            value.Contains('\\') ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Port != 443 ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            value.Contains('?') ||
            value.Contains('#') ||
            uri.AbsolutePath.Contains('%'))
        {
            return false;
        }

        var path = uri.AbsolutePath;
        if (path.Length == 0 || path[0] != '/')
        {
            return false;
        }

        var segments = path[1..].Split('/');
        if (segments.Length != 2)
        {
            return false;
        }

        var owner = segments[0];
        var repositoryName = segments[1];
        if (!IsValidOwner(owner))
        {
            return false;
        }

        if (repositoryName.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            repositoryName = repositoryName[..^4];
        }

        if (!IsValidRepositoryName(repositoryName))
        {
            return false;
        }

        reference = new GitHubRepositoryReference(owner, repositoryName);
        return true;
    }

    private static bool IsValidOwner(string owner) =>
        owner.Length is > 0 and <= 39 &&
        IsAsciiLetterOrDigit(owner[0]) &&
        IsAsciiLetterOrDigit(owner[^1]) &&
        owner.All(character => IsAsciiLetterOrDigit(character) || character == '-');

    private static bool IsValidRepositoryName(string repositoryName) =>
        repositoryName.Length is > 0 and <= 100 &&
        repositoryName is not "." and not ".." &&
        repositoryName.All(character =>
            IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static bool IsAsciiLetterOrDigit(char character) =>
        character is >= 'a' and <= 'z' or
            >= 'A' and <= 'Z' or
            >= '0' and <= '9';
}
