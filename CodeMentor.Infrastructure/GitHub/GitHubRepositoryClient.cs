using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CodeMentor.Application.GitHub;

namespace CodeMentor.Infrastructure.GitHub;

public sealed class GitHubRepositoryClient : IGitHubRepositoryClient
{
    private static readonly Uri ApiBaseUri = new("https://api.github.com/");
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        MaxDepth = 16
    };

    private readonly HttpClient _httpClient;
    private readonly GitHubRepositoryTreeLimits _treeLimits;
    private readonly GitHubRepositoryFileContentLimits _fileContentLimits;
    private readonly GitHubRepositoryFileSelectionPolicy _fileSelectionPolicy;

    public GitHubRepositoryClient(
        HttpClient httpClient,
        GitHubRepositoryTreeLimits? treeLimits = null,
        GitHubRepositoryFileContentLimits? fileContentLimits = null,
        GitHubRepositoryFileSelectionPolicy? fileSelectionPolicy = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _treeLimits = treeLimits ?? new GitHubRepositoryTreeLimits();
        _fileContentLimits = fileContentLimits ?? new GitHubRepositoryFileContentLimits();
        _fileSelectionPolicy = fileSelectionPolicy ?? new GitHubRepositoryFileSelectionPolicy();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CodeMentorAI/1.0");
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.Remove("X-GitHub-Api-Version");
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task<GitHubRepositoryMetadata> GetPublicRepositoryAsync(
        GitHubRepositoryReference repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var requestUri = new Uri(
            ApiBaseUri,
            $"repos/{Uri.EscapeDataString(repository.Owner)}/{Uri.EscapeDataString(repository.RepositoryName)}");

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        HttpResponseMessage response;

        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GitHubRepositoryClientException(
                GitHubRepositoryClientError.Timeout,
                "The GitHub API request timed out.");
        }
        catch (HttpRequestException exception)
        {
            throw new GitHubRepositoryClientException(
                GitHubRepositoryClientError.NetworkError,
                "The GitHub API request could not be completed.",
                innerException: exception);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw CreateResponseException(
                    GitHubRepositoryClientError.NotFound,
                    "The public GitHub repository was not found.",
                    response);
            }

            if (response.StatusCode is HttpStatusCode.Forbidden or (HttpStatusCode)429)
            {
                throw CreateRateLimitException(response);
            }

            if ((int)response.StatusCode >= 500)
            {
                throw CreateResponseException(
                    GitHubRepositoryClientError.ServerError,
                    "The GitHub API returned a server error.",
                    response);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw CreateResponseException(
                    GitHubRepositoryClientError.UnexpectedStatus,
                    "The GitHub API returned an unexpected status code.",
                    response);
            }

            return await ReadMetadataAsync(response, repository, cancellationToken);
        }
    }

    public async Task<GitHubRepositoryTree> GetRepositoryTreeAsync(
        GitHubRepositoryReference repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var metadata = await GetPublicRepositoryAsync(repository, cancellationToken);
        if (!IsValidGitRef(metadata.DefaultBranch))
        {
            throw InvalidTreeResponse("The GitHub repository returned an invalid default branch.");
        }

        var branchSegments = metadata.DefaultBranch
            .Split('/')
            .Select(Uri.EscapeDataString);
        var referencePath =
            $"repos/{Uri.EscapeDataString(repository.Owner)}/" +
            $"{Uri.EscapeDataString(repository.RepositoryName)}/git/ref/heads/" +
            string.Join('/', branchSegments);
        using var referenceJson = await GetJsonResponseAsync(
            new Uri(ApiBaseUri, referencePath),
            cancellationToken);
        var commitSha = ReadReferenceCommitSha(referenceJson);

        using var commitJson = await GetJsonResponseAsync(
            new Uri(
                ApiBaseUri,
                $"repos/{Uri.EscapeDataString(repository.Owner)}/" +
                $"{Uri.EscapeDataString(repository.RepositoryName)}/git/commits/{commitSha}"),
            cancellationToken);
        var treeSha = ReadCommitTreeSha(commitJson);

        using var treeJson = await GetJsonResponseAsync(
            new Uri(
                ApiBaseUri,
                $"repos/{Uri.EscapeDataString(repository.Owner)}/" +
                $"{Uri.EscapeDataString(repository.RepositoryName)}/git/trees/{treeSha}?recursive=1"),
            cancellationToken);

        return ReadTree(treeJson, repository);
    }

    public async Task<IReadOnlyList<GitHubRepositoryFileContent>> GetRepositoryFilesAsync(
        GitHubRepositoryReference repository,
        GitHubRepositoryTree tree,
        IReadOnlyCollection<string> selectedPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(selectedPaths);

        var selectedEntries = ValidateSelections(repository, tree, selectedPaths);
        if (selectedEntries.Count == 0)
        {
            return [];
        }

        using var verifiedTreeJson = await GetJsonResponseAsync(
            new Uri(
                ApiBaseUri,
                $"repos/{Uri.EscapeDataString(repository.Owner)}/" +
                $"{Uri.EscapeDataString(repository.RepositoryName)}/git/trees/{tree.Sha}?recursive=1"),
            cancellationToken);
        var verifiedTree = ReadTree(verifiedTreeJson, repository);
        EnsureTreeMatches(tree, verifiedTree, selectedEntries);

        var contents = new List<GitHubRepositoryFileContent>(selectedEntries.Count);
        foreach (var entry in selectedEntries)
        {
            using var blobJson = await GetJsonResponseAsync(
                new Uri(
                    ApiBaseUri,
                    $"repos/{Uri.EscapeDataString(repository.Owner)}/" +
                    $"{Uri.EscapeDataString(repository.RepositoryName)}/git/blobs/{entry.Sha}"),
                cancellationToken,
                _fileContentLimits.MaximumBlobResponseBytes,
                GitHubRepositoryClientError.ResponseTooLarge);

            contents.Add(ReadBlob(entry, blobJson));
        }

        return contents.AsReadOnly();
    }

    private async Task<JsonDocument> GetJsonResponseAsync(
        Uri requestUri,
        CancellationToken cancellationToken,
        int? maximumResponseBytes = null,
        GitHubRepositoryClientError responseTooLargeError = GitHubRepositoryClientError.TreeTooLarge)
    {
        using var timeoutSource = new CancellationTokenSource();
        if (_httpClient.Timeout != Timeout.InfiniteTimeSpan)
        {
            timeoutSource.CancelAfter(_httpClient.Timeout);
        }

        using var requestCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        var requestCancellationToken = requestCancellationSource.Token;
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        HttpResponseMessage response;

        try
        {
            response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                requestCancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GitHubRepositoryClientException(
                GitHubRepositoryClientError.Timeout,
                "The GitHub API request timed out.");
        }
        catch (HttpRequestException exception)
        {
            throw new GitHubRepositoryClientException(
                GitHubRepositoryClientError.NetworkError,
                "The GitHub API request could not be completed.",
                innerException: exception);
        }

        using (response)
        {
            EnsureSuccessStatusCode(response);
            try
            {
                var responseBytes = await ReadBoundedResponseAsync(
                    response,
                    requestCancellationToken,
                    maximumResponseBytes ?? _treeLimits.MaximumResponseBytes,
                    responseTooLargeError);
                return JsonDocument.Parse(responseBytes, JsonOptions);
            }
            catch (JsonException exception)
            {
                throw new GitHubRepositoryClientException(
                    GitHubRepositoryClientError.InvalidResponse,
                    "The GitHub API returned malformed repository tree data.",
                    response.StatusCode,
                    innerException: exception);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new GitHubRepositoryClientException(
                    GitHubRepositoryClientError.Timeout,
                    "Reading the GitHub API response timed out.",
                    response.StatusCode);
            }
            catch (IOException exception)
            {
                throw new GitHubRepositoryClientException(
                    GitHubRepositoryClientError.NetworkError,
                    "The GitHub API response could not be read.",
                    response.StatusCode,
                    innerException: exception);
            }
            catch (HttpRequestException exception)
            {
                throw new GitHubRepositoryClientException(
                    GitHubRepositoryClientError.NetworkError,
                    "The GitHub API response could not be read.",
                    response.StatusCode,
                    innerException: exception);
            }
        }
    }

    private async Task<byte[]> ReadBoundedResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken,
        int maximumResponseBytes,
        GitHubRepositoryClientError tooLargeError)
    {
        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength > maximumResponseBytes)
        {
            throw ResponseTooLarge(tooLargeError);
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream(
            Math.Min(maximumResponseBytes, contentLength is > 0
                ? (int)contentLength.Value
                : 4096));
        var chunk = new byte[8192];
        var totalBytes = 0;

        while (true)
        {
            var bytesRead = await source.ReadAsync(chunk, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            if (bytesRead > maximumResponseBytes - totalBytes)
            {
                throw ResponseTooLarge(tooLargeError);
            }

            await buffer.WriteAsync(chunk.AsMemory(0, bytesRead), cancellationToken);
            totalBytes += bytesRead;
        }

        return buffer.ToArray();
    }

    private static void EnsureSuccessStatusCode(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw CreateResponseException(
                GitHubRepositoryClientError.NotFound,
                "The public GitHub repository or reference was not found.",
                response);
        }

        if (response.StatusCode is HttpStatusCode.Forbidden or (HttpStatusCode)429)
        {
            throw CreateRateLimitException(response);
        }

        if ((int)response.StatusCode >= 500)
        {
            throw CreateResponseException(
                GitHubRepositoryClientError.ServerError,
                "The GitHub API returned a server error.",
                response);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw CreateResponseException(
                GitHubRepositoryClientError.UnexpectedStatus,
                "The GitHub API returned an unexpected status code.",
                response);
        }
    }

    private static string ReadReferenceCommitSha(JsonDocument document)
    {
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("object", out var gitObject) ||
            gitObject.ValueKind != JsonValueKind.Object ||
            !TryGetString(gitObject, "type", out var objectType) ||
            !string.Equals(objectType, "commit", StringComparison.Ordinal) ||
            !TryGetString(gitObject, "sha", out var sha) ||
            !IsValidSha(sha))
        {
            throw InvalidTreeResponse("The GitHub API returned an invalid branch reference.");
        }

        return sha;
    }

    private static string ReadCommitTreeSha(JsonDocument document)
    {
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("tree", out var tree) ||
            tree.ValueKind != JsonValueKind.Object ||
            !TryGetString(tree, "sha", out var sha) ||
            !IsValidSha(sha))
        {
            throw InvalidTreeResponse("The GitHub API returned an invalid commit tree reference.");
        }

        return sha;
    }

    private GitHubRepositoryTree ReadTree(
        JsonDocument document,
        GitHubRepositoryReference repository)
    {
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !TryGetString(root, "sha", out var treeSha) ||
            !IsValidSha(treeSha) ||
            !root.TryGetProperty("truncated", out var truncated) ||
            truncated.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            !root.TryGetProperty("tree", out var tree) ||
            tree.ValueKind != JsonValueKind.Array)
        {
            throw InvalidTreeResponse("The GitHub API returned an invalid tree response.");
        }

        if (truncated.GetBoolean())
        {
            throw new GitHubRepositoryClientException(
                GitHubRepositoryClientError.TreeTruncated,
                "GitHub returned a truncated repository tree.");
        }

        if (tree.GetArrayLength() > _treeLimits.MaximumEntries)
        {
            throw TreeTooLarge();
        }

        var entries = new List<GitHubRepositoryTreeEntry>(tree.GetArrayLength());
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in tree.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !TryGetString(item, "path", out var path) ||
                !IsValidTreePath(path) ||
                !TryGetString(item, "type", out var typeValue) ||
                !TryGetEntryType(typeValue, out var type) ||
                !TryGetString(item, "sha", out var sha) ||
                !IsValidSha(sha))
            {
                throw InvalidTreeResponse("The GitHub API returned an invalid tree entry.");
            }

            long? size = null;
            if (item.TryGetProperty("size", out var sizeElement) &&
                sizeElement.ValueKind != JsonValueKind.Null)
            {
                if (sizeElement.ValueKind != JsonValueKind.Number ||
                    !sizeElement.TryGetInt64(out var parsedSize) ||
                    parsedSize < 0)
                {
                    throw InvalidTreeResponse("The GitHub API returned an invalid tree entry size.");
                }

                size = parsedSize;
            }

            if (!paths.Add(path))
            {
                throw InvalidTreeResponse("The GitHub API returned duplicate tree paths.");
            }

            entries.Add(new GitHubRepositoryTreeEntry(path, type, size, sha));
        }

        return new GitHubRepositoryTree(repository.Owner, repository.RepositoryName, treeSha, entries);
    }

    private List<GitHubRepositoryTreeEntry> ValidateSelections(
        GitHubRepositoryReference repository,
        GitHubRepositoryTree tree,
        IReadOnlyCollection<string> selectedPaths)
    {
        if (!string.Equals(tree.Owner, repository.Owner, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(tree.RepositoryName, repository.RepositoryName, StringComparison.OrdinalIgnoreCase) ||
            !IsValidSha(tree.Sha) ||
            tree.Entries is null)
        {
            throw InvalidSelection("The supplied tree does not belong to the requested repository.");
        }

        if (tree.Entries.Count > _treeLimits.MaximumEntries)
        {
            throw TreeTooLarge();
        }

        if (selectedPaths.Count > _fileContentLimits.MaximumSelectedFiles)
        {
            throw new GitHubRepositoryClientException(
                GitHubRepositoryClientError.TooManyFiles,
                "The selected file count exceeds the configured limit.");
        }

        var entriesByPath = new Dictionary<string, GitHubRepositoryTreeEntry>(StringComparer.Ordinal);
        foreach (var entry in tree.Entries)
        {
            if (entry is null ||
                !IsValidTreePath(entry.Path) ||
                !IsValidSha(entry.Sha) ||
                !Enum.IsDefined(entry.Type) ||
                entry.Size < 0 ||
                !entriesByPath.TryAdd(entry.Path, entry))
            {
                throw InvalidSelection("The supplied repository tree contains invalid entries.");
            }
        }

        var selectedEntries = new List<GitHubRepositoryTreeEntry>(selectedPaths.Count);
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        long totalBytes = 0;
        foreach (var path in selectedPaths)
        {
            if (!IsValidTreePath(path) ||
                !seenPaths.Add(path) ||
                !entriesByPath.TryGetValue(path, out var entry) ||
                entry.Type != GitHubRepositoryTreeEntryType.Blob ||
                entry.Size is null ||
                !_fileSelectionPolicy.Allows(path))
            {
                throw InvalidSelection("One or more selected paths are invalid or not permitted.");
            }

            if (entry.Size > _fileContentLimits.MaximumFileBytes)
            {
                throw new GitHubRepositoryClientException(
                    GitHubRepositoryClientError.FileTooLarge,
                    "A selected file exceeds the configured per-file limit.");
            }

            totalBytes += entry.Size.Value;
            if (totalBytes > _fileContentLimits.MaximumTotalBytes)
            {
                throw new GitHubRepositoryClientException(
                    GitHubRepositoryClientError.TotalDownloadTooLarge,
                    "The selected files exceed the configured total download limit.");
            }

            selectedEntries.Add(entry);
        }

        return selectedEntries;
    }

    private static void EnsureTreeMatches(
        GitHubRepositoryTree suppliedTree,
        GitHubRepositoryTree verifiedTree,
        IReadOnlyList<GitHubRepositoryTreeEntry> selectedEntries)
    {
        if (!string.Equals(suppliedTree.Sha, verifiedTree.Sha, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(suppliedTree.Owner, verifiedTree.Owner, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                suppliedTree.RepositoryName,
                verifiedTree.RepositoryName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw InvalidSelection("The supplied tree could not be verified against GitHub.");
        }

        var verifiedEntries = verifiedTree.Entries.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        foreach (var entry in selectedEntries)
        {
            if (!verifiedEntries.TryGetValue(entry.Path, out var verifiedEntry) ||
                verifiedEntry != entry)
            {
                throw InvalidSelection("A selected tree entry could not be verified against GitHub.");
            }
        }
    }

    private GitHubRepositoryFileContent ReadBlob(
        GitHubRepositoryTreeEntry entry,
        JsonDocument document)
    {
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !TryGetString(root, "sha", out var sha) ||
            !IsValidSha(sha) ||
            !string.Equals(sha, entry.Sha, StringComparison.OrdinalIgnoreCase) ||
            !TryGetString(root, "encoding", out var encoding) ||
            !string.Equals(encoding, "base64", StringComparison.Ordinal) ||
            !root.TryGetProperty("size", out var sizeElement) ||
            sizeElement.ValueKind != JsonValueKind.Number ||
            !sizeElement.TryGetInt64(out var size) ||
            size < 0 ||
            size != entry.Size ||
            !root.TryGetProperty("content", out var contentElement) ||
            contentElement.ValueKind != JsonValueKind.String)
        {
            throw InvalidResponse("The GitHub API returned invalid blob metadata.");
        }

        byte[] contentBytes;
        try
        {
            contentBytes = Convert.FromBase64String(contentElement.GetString()!);
        }
        catch (FormatException exception)
        {
            throw new GitHubRepositoryClientException(
                GitHubRepositoryClientError.InvalidResponse,
                "The GitHub API returned malformed base64 file content.",
                innerException: exception);
        }

        if (contentBytes.LongLength > _fileContentLimits.MaximumFileBytes ||
            contentBytes.LongLength != size)
        {
            throw new GitHubRepositoryClientException(
                GitHubRepositoryClientError.InvalidResponse,
                "The GitHub API file content size does not match its metadata.");
        }

        var offset = contentBytes.Length >= 3 &&
                     contentBytes[0] == 0xEF &&
                     contentBytes[1] == 0xBB &&
                     contentBytes[2] == 0xBF
            ? 3
            : 0;
        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(contentBytes, offset, contentBytes.Length - offset);
        }
        catch (DecoderFallbackException exception)
        {
            throw new GitHubRepositoryClientException(
                GitHubRepositoryClientError.UnsupportedContent,
                "The selected GitHub file is not valid UTF-8 text.",
                innerException: exception);
        }

        if (text.Contains('\0'))
        {
            throw new GitHubRepositoryClientException(
                GitHubRepositoryClientError.UnsupportedContent,
                "The selected GitHub file appears to contain binary content.");
        }

        return new GitHubRepositoryFileContent(entry.Path, entry.Sha, size, text);
    }

    private static bool TryGetEntryType(
        string value,
        out GitHubRepositoryTreeEntryType type)
    {
        type = value switch
        {
            "blob" => GitHubRepositoryTreeEntryType.Blob,
            "tree" => GitHubRepositoryTreeEntryType.Tree,
            "commit" => GitHubRepositoryTreeEntryType.Commit,
            _ => (GitHubRepositoryTreeEntryType)(-1)
        };
        return Enum.IsDefined(type);
    }

    private static bool IsValidGitRef(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 255 ||
            value == "@" ||
            value.StartsWith('/') ||
            value.EndsWith('/') ||
            value.EndsWith('.') ||
            value.Contains("..", StringComparison.Ordinal) ||
            value.Contains("@{", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (char.IsControl(character) ||
                char.IsWhiteSpace(character) ||
                character is '~' or '^' or ':' or '?' or '*' or '[' or '\\')
            {
                return false;
            }
        }

        return value.Split('/').All(segment =>
            segment.Length > 0 &&
            !segment.StartsWith('.') &&
            !segment.EndsWith(".lock", StringComparison.Ordinal));
    }

    private static bool IsValidTreePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.Length > 4096 ||
            path.StartsWith('/') ||
            path.EndsWith('/') ||
            path.Contains('\\') ||
            (path.Length >= 2 &&
             char.IsAsciiLetter(path[0]) &&
             path[1] == ':'))
        {
            return false;
        }

        foreach (var character in path)
        {
            if (char.IsControl(character))
            {
                return false;
            }
        }

        return path.Split('/').All(segment =>
            segment.Length > 0 && segment is not "." and not "..");
    }

    private static bool IsValidSha(string? sha) =>
        sha is { Length: 40 } && sha.All(Uri.IsHexDigit);

    private static GitHubRepositoryClientException InvalidTreeResponse(string message) =>
        new(GitHubRepositoryClientError.InvalidResponse, message);

    private static GitHubRepositoryClientException InvalidSelection(string message) =>
        new(GitHubRepositoryClientError.InvalidSelection, message);

    private static GitHubRepositoryClientException InvalidResponse(string message) =>
        new(GitHubRepositoryClientError.InvalidResponse, message);

    private static GitHubRepositoryClientException TreeTooLarge() =>
        new(
            GitHubRepositoryClientError.TreeTooLarge,
            "The GitHub repository tree exceeds the configured safety limits.");

    private static GitHubRepositoryClientException ResponseTooLarge(
        GitHubRepositoryClientError error) =>
        new(error, "The GitHub API response exceeds the configured safety limit.");

    private static async Task<GitHubRepositoryMetadata> ReadMetadataAsync(
        HttpResponseMessage response,
        GitHubRepositoryReference requestedRepository,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(
                contentStream,
                JsonOptions,
                cancellationToken);

            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetString(root, "name", out var name) ||
                !TryGetString(root, "html_url", out var htmlUrl) ||
                !TryGetString(root, "default_branch", out var defaultBranch) ||
                !TryGetString(root, "visibility", out var visibility) ||
                !root.TryGetProperty("owner", out var ownerElement) ||
                ownerElement.ValueKind != JsonValueKind.Object ||
                !TryGetString(ownerElement, "login", out var owner))
            {
                throw InvalidResponse();
            }

            if (root.TryGetProperty("private", out var isPrivateElement) &&
                (isPrivateElement.ValueKind != JsonValueKind.True &&
                 isPrivateElement.ValueKind != JsonValueKind.False))
            {
                throw InvalidResponse();
            }

            var isPrivate = isPrivateElement.ValueKind == JsonValueKind.True;
            if (isPrivate || !string.Equals(visibility, "public", StringComparison.OrdinalIgnoreCase))
            {
                throw new GitHubRepositoryClientException(
                    GitHubRepositoryClientError.NotPublic,
                    "The GitHub repository is not public.");
            }

            if (owner.Length > 39 ||
                name.Length > 100 ||
                defaultBranch.Length > 255 ||
                !string.Equals(owner, requestedRepository.Owner, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(name, requestedRepository.RepositoryName, StringComparison.OrdinalIgnoreCase) ||
                !GitHubRepositoryReference.TryParse(htmlUrl, out var canonicalReference) ||
                canonicalReference is null ||
                !string.Equals(owner, canonicalReference.Owner, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(name, canonicalReference.RepositoryName, StringComparison.OrdinalIgnoreCase))
            {
                throw InvalidResponse();
            }

            return new GitHubRepositoryMetadata(
                owner,
                name,
                canonicalReference.CanonicalUrl,
                defaultBranch,
                "public");
        }
        catch (JsonException exception)
        {
            throw new GitHubRepositoryClientException(
                GitHubRepositoryClientError.InvalidResponse,
                "The GitHub API returned malformed repository metadata.",
                response.StatusCode,
                innerException: exception);
        }
        catch (IOException exception)
        {
            throw new GitHubRepositoryClientException(
                GitHubRepositoryClientError.NetworkError,
                "The GitHub API response could not be read.",
                response.StatusCode,
                innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            throw new GitHubRepositoryClientException(
                GitHubRepositoryClientError.NetworkError,
                "The GitHub API response could not be read.",
                response.StatusCode,
                innerException: exception);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GitHubRepositoryClientException(
                GitHubRepositoryClientError.Timeout,
                "Reading the GitHub API response timed out.",
                response.StatusCode);
        }
    }

    private static GitHubRepositoryClientException CreateRateLimitException(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        var retryAfterAtUtc = retryAfter?.Date;
        var retryAfterDelay = retryAfter?.Delta;
        var rateLimitResetAtUtc = TryGetRateLimitReset(response);

        if (retryAfterDelay is null && retryAfterAtUtc is not null)
        {
            retryAfterDelay = retryAfterAtUtc.Value - DateTimeOffset.UtcNow;
            if (retryAfterDelay < TimeSpan.Zero)
            {
                retryAfterDelay = TimeSpan.Zero;
            }
        }

        return new GitHubRepositoryClientException(
            GitHubRepositoryClientError.RateLimited,
            "The GitHub API rate limit has been reached.",
            response.StatusCode,
            retryAfterDelay,
            retryAfterAtUtc,
            rateLimitResetAtUtc);
    }

    private static DateTimeOffset? TryGetRateLimitReset(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("X-RateLimit-Reset", out var values))
        {
            return null;
        }

        var value = values.FirstOrDefault();
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var unixSeconds))
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(value = property.GetString()!);
    }

    private static GitHubRepositoryClientException CreateResponseException(
        GitHubRepositoryClientError error,
        string message,
        HttpResponseMessage response) =>
        new(error, message, response.StatusCode);

    private static GitHubRepositoryClientException InvalidResponse() =>
        new(
            GitHubRepositoryClientError.InvalidResponse,
            "The GitHub API returned invalid repository metadata.");
}
