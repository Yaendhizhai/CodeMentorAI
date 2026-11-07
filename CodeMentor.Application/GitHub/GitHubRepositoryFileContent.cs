namespace CodeMentor.Application.GitHub;

public sealed record GitHubRepositoryFileContent(
    string Path,
    string Sha,
    long Size,
    string Content);

public sealed record GitHubRepositoryFileContentLimits
{
    public const int DefaultMaximumFileBytes = 1024 * 1024;
    public const int DefaultMaximumTotalBytes = 10 * 1024 * 1024;
    public const int DefaultMaximumSelectedFiles = 50;
    public const int DefaultMaximumBlobResponseBytes = 2 * 1024 * 1024;

    public GitHubRepositoryFileContentLimits(
        int maximumFileBytes = DefaultMaximumFileBytes,
        int maximumTotalBytes = DefaultMaximumTotalBytes,
        int maximumSelectedFiles = DefaultMaximumSelectedFiles,
        int maximumBlobResponseBytes = DefaultMaximumBlobResponseBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFileBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumTotalBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSelectedFiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBlobResponseBytes);
        MaximumFileBytes = maximumFileBytes;
        MaximumTotalBytes = maximumTotalBytes;
        MaximumSelectedFiles = maximumSelectedFiles;
        MaximumBlobResponseBytes = maximumBlobResponseBytes;
    }

    public int MaximumFileBytes { get; }

    public int MaximumTotalBytes { get; }

    public int MaximumSelectedFiles { get; }

    public int MaximumBlobResponseBytes { get; }
}

public sealed class GitHubRepositoryFileSelectionPolicy
{
    private static readonly string[] DefaultAllowedExtensions =
    [
        ".bat", ".c", ".cjs", ".cpp", ".cs", ".cshtml", ".css", ".go", ".h",
        ".hpp", ".html", ".java", ".js", ".json", ".jsx", ".kt", ".kts", ".md",
        ".mjs", ".ps1", ".py", ".razor", ".rs", ".sass", ".scss", ".sh", ".sql",
        ".svelte", ".tf", ".toml", ".ts", ".tsx", ".txt", ".vue", ".xml", ".yaml",
        ".yml"
    ];

    private static readonly string[] DefaultExcludedFileNames =
    [
        ".env", "credentials", "id_dsa", "id_ecdsa", "id_ed25519", "id_rsa",
        "secrets", "secret"
    ];

    private static readonly string[] DefaultExcludedExtensions =
    [
        ".asc", ".cer", ".crt", ".der", ".gpg", ".jks", ".key", ".keystore",
        ".p12", ".p7b", ".p7c", ".pem", ".pfx", ".ppk"
    ];

    private readonly HashSet<string> _allowedExtensions;
    private readonly HashSet<string> _excludedFileNames;
    private readonly HashSet<string> _excludedExtensions;

    public GitHubRepositoryFileSelectionPolicy(
        IEnumerable<string>? allowedExtensions = null,
        IEnumerable<string>? excludedFileNames = null,
        IEnumerable<string>? excludedExtensions = null)
    {
        _allowedExtensions = CreateSet(allowedExtensions ?? DefaultAllowedExtensions);
        _excludedFileNames = CreateSet(excludedFileNames ?? DefaultExcludedFileNames);
        _excludedExtensions = CreateSet(excludedExtensions ?? DefaultExcludedExtensions);
    }

    public bool Allows(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fileName = path[(path.LastIndexOf('/') + 1)..];
        if (fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase) ||
            _excludedFileNames.Contains(fileName) ||
            fileName.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("private_key", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var extension = GetExtension(fileName);
        return !_excludedExtensions.Contains(extension) &&
               _allowedExtensions.Contains(extension);
    }

    private static HashSet<string> CreateSet(IEnumerable<string> values) =>
        new(values.Select(value =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            return value.Trim().ToLowerInvariant();
        }), StringComparer.OrdinalIgnoreCase);

    private static string GetExtension(string fileName)
    {
        var extensionIndex = fileName.LastIndexOf('.');
        return extensionIndex <= 0 ? string.Empty : fileName[extensionIndex..];
    }
}
