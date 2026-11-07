using System.Net;

namespace CodeMentor.Application.GitHub;

public enum GitHubRepositoryClientError
{
    NotFound,
    RateLimited,
    ServerError,
    NetworkError,
    Timeout,
    InvalidResponse,
    NotPublic,
    TreeTruncated,
    TreeTooLarge,
    InvalidSelection,
    TooManyFiles,
    FileTooLarge,
    TotalDownloadTooLarge,
    ResponseTooLarge,
    UnsupportedContent,
    UnexpectedStatus
}

public sealed class GitHubRepositoryClientException : Exception
{
    public GitHubRepositoryClientException(
        GitHubRepositoryClientError error,
        string message,
        HttpStatusCode? statusCode = null,
        TimeSpan? retryAfterDelay = null,
        DateTimeOffset? retryAfterAtUtc = null,
        DateTimeOffset? rateLimitResetAtUtc = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
        StatusCode = statusCode;
        RetryAfterDelay = retryAfterDelay;
        RetryAfterAtUtc = retryAfterAtUtc;
        RateLimitResetAtUtc = rateLimitResetAtUtc;
    }

    public GitHubRepositoryClientError Error { get; }

    public HttpStatusCode? StatusCode { get; }

    public TimeSpan? RetryAfterDelay { get; }

    public DateTimeOffset? RetryAfterAtUtc { get; }

    public DateTimeOffset? RateLimitResetAtUtc { get; }
}
