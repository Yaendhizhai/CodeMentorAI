using System.ComponentModel.DataAnnotations;
using System.Runtime.ExceptionServices;
using CodeMentor.Application.Abstractions.Persistence;
using CodeMentor.Application.GitHub;
using CodeMentor.Core.Entities;
using CodeMentor.Core.Enums;

namespace CodeMentor.Application.RepositoryIngestion;

public sealed class RepositoryIngestionService(
    IRepositoryProjectRepository projectRepository,
    IRepositoryFileRepository fileRepository,
    ICodeChunkRepository chunkRepository,
    IAnalysisSessionRepository sessionRepository,
    IUnitOfWork unitOfWork,
    IGitHubRepositoryClient gitHubRepositoryClient,
    GitHubRepositoryFileContentLimits contentLimits,
    GitHubRepositoryFileSelectionPolicy selectionPolicy) : IRepositoryIngestionService
{
    private const int LinesPerChunk = 200;
    private const string SessionPersistenceFailureDataKey =
        "CodeMentor.RepositoryIngestion.SessionPersistenceFailure";

    public async Task<RepositoryIngestionResponse> IngestAsync(
        Guid repositoryProjectId,
        CancellationToken cancellationToken = default)
    {
        if (repositoryProjectId == Guid.Empty)
        {
            throw new ArgumentException("A repository project ID is required.", nameof(repositoryProjectId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var project = await projectRepository.GetByIdAsync(repositoryProjectId, cancellationToken);
        if (project is null)
        {
            throw new KeyNotFoundException("The repository project was not found.");
        }

        if (await fileRepository.HasAnyByProjectIdAsync(repositoryProjectId, cancellationToken))
        {
            throw new InvalidOperationException("The repository project has already been ingested.");
        }

        if (!GitHubRepositoryReference.TryParse(project.RepositoryUrl, out var reference) ||
            reference is null)
        {
            throw new ValidationException(
                "The repository project must have a valid public GitHub repository URL.");
        }

        var session = new AnalysisSession(repositoryProjectId);
        session.MarkRunning(DateTimeOffset.UtcNow);
        await sessionRepository.AddAsync(session, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        GitHubRepositoryTree tree;
        IReadOnlyList<GitHubRepositoryFileContent> contents;
        List<IngestedFile> files;

        try
        {
            tree = await gitHubRepositoryClient.GetRepositoryTreeAsync(reference, cancellationToken);
            var selectedEntries = SelectEntries(reference, tree);
            var selectedPaths = selectedEntries.Select(entry => entry.Path).ToArray();
            contents = await gitHubRepositoryClient.GetRepositoryFilesAsync(
                reference,
                tree,
                selectedPaths,
                cancellationToken);
            files = CreateIngestedFiles(repositoryProjectId, selectedEntries, contents);
        }
        catch (Exception exception)
        {
            await PersistTerminalStateBeforeTransactionAsync(session, exception, cancellationToken);
            RethrowPreservingCallerCancellation(exception, cancellationToken);
            throw;
        }

        try
        {
            await unitOfWork.ExecuteInTransactionAsync(async transactionToken =>
            {
                foreach (var ingestedFile in files)
                {
                    await fileRepository.AddAsync(ingestedFile.File, transactionToken);
                    foreach (var chunk in ingestedFile.Chunks)
                    {
                        await chunkRepository.AddAsync(chunk, transactionToken);
                    }
                }

                session.MarkCompleted(DateTimeOffset.UtcNow);
                await unitOfWork.SaveChangesAsync(transactionToken);
            }, cancellationToken);
        }
        catch (Exception exception)
        {
            await PersistTerminalStateAfterRollbackAsync(session.Id, exception, cancellationToken);
            RethrowPreservingCallerCancellation(exception, cancellationToken);
            throw;
        }

        return new RepositoryIngestionResponse(
            session.Id,
            repositoryProjectId,
            tree.Sha,
            files.Count,
            files.Sum(file => file.Chunks.Count),
            session.CompletedAtUtc!.Value);
    }

    private List<GitHubRepositoryTreeEntry> SelectEntries(
        GitHubRepositoryReference reference,
        GitHubRepositoryTree tree)
    {
        if (!string.Equals(tree.Owner, reference.Owner, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(tree.RepositoryName, reference.RepositoryName, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(tree.Sha) ||
            tree.Entries is null)
        {
            throw InvalidGitHubResponse("GitHub returned a tree that does not match the registered repository.");
        }

        var selectedEntries = new List<GitHubRepositoryTreeEntry>();
        var selectedPaths = new HashSet<string>(StringComparer.Ordinal);
        long knownTotalBytes = 0;

        foreach (var entry in tree.Entries)
        {
            if (entry.Type != GitHubRepositoryTreeEntryType.Blob)
            {
                continue;
            }

            bool allowed;
            try
            {
                allowed = selectionPolicy.Allows(entry.Path);
            }
            catch (ArgumentException exception)
            {
                throw InvalidGitHubResponse(
                    "GitHub returned an invalid source-file path.",
                    exception);
            }

            if (!allowed)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.Sha) || !selectedPaths.Add(entry.Path))
            {
                throw InvalidGitHubResponse("GitHub returned duplicate or invalid source-file metadata.");
            }

            if (entry.Size is < 0)
            {
                throw InvalidGitHubResponse("GitHub returned a negative source-file size.");
            }

            if (entry.Size > contentLimits.MaximumFileBytes)
            {
                throw FileLimitError(
                    GitHubRepositoryClientError.FileTooLarge,
                    "An eligible repository file exceeds the configured per-file limit.");
            }

            if (entry.Size is long size)
            {
                if (size > contentLimits.MaximumTotalBytes - knownTotalBytes)
                {
                    throw FileLimitError(
                        GitHubRepositoryClientError.TotalDownloadTooLarge,
                        "Eligible repository files exceed the configured aggregate-size limit.");
                }

                knownTotalBytes += size;
            }

            selectedEntries.Add(entry);
            if (selectedEntries.Count > contentLimits.MaximumSelectedFiles)
            {
                throw FileLimitError(
                    GitHubRepositoryClientError.TooManyFiles,
                    "Eligible repository files exceed the configured selection-count limit.");
            }
        }

        return selectedEntries
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ToList();
    }

    private List<IngestedFile> CreateIngestedFiles(
        Guid repositoryProjectId,
        IReadOnlyList<GitHubRepositoryTreeEntry> selectedEntries,
        IReadOnlyList<GitHubRepositoryFileContent> contents)
    {
        if (contents.Count != selectedEntries.Count)
        {
            throw InvalidGitHubResponse("GitHub did not return every selected source file.");
        }

        var selectedByPath = selectedEntries.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        var contentPaths = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<IngestedFile>(contents.Count);
        long totalBytes = 0;

        foreach (var content in contents.OrderBy(item => item.Path, StringComparer.Ordinal))
        {
            if (!selectedByPath.TryGetValue(content.Path, out var entry) ||
                !contentPaths.Add(content.Path) ||
                !string.Equals(content.Sha, entry.Sha, StringComparison.OrdinalIgnoreCase))
            {
                throw InvalidGitHubResponse(
                    "GitHub returned content that does not match the verified repository tree.");
            }

            if (content.Size < 0 || content.Size > contentLimits.MaximumFileBytes)
            {
                throw FileLimitError(
                    GitHubRepositoryClientError.FileTooLarge,
                    "A selected repository file exceeds the configured per-file limit.");
            }

            if (content.Size > contentLimits.MaximumTotalBytes - totalBytes)
            {
                throw FileLimitError(
                    GitHubRepositoryClientError.TotalDownloadTooLarge,
                    "Selected repository files exceed the configured aggregate-size limit.");
            }

            totalBytes += content.Size;
            var file = new RepositoryFile(
                repositoryProjectId,
                content.Path,
                contentHash: entry.Sha,
                sizeInBytes: content.Size);
            result.Add(new IngestedFile(file, CreateChunks(file.Id, content.Content)));
        }

        return result;
    }

    private static IReadOnlyList<CodeChunk> CreateChunks(Guid repositoryFileId, string content)
    {
        if (content.Length == 0)
        {
            return [];
        }

        var normalizedContent = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var endsWithNewLine = normalizedContent.EndsWith('\n');
        var lines = normalizedContent.Split('\n');
        if (endsWithNewLine)
        {
            Array.Resize(ref lines, lines.Length - 1);
        }

        var chunks = new List<CodeChunk>();
        for (var startIndex = 0; startIndex < lines.Length; startIndex += LinesPerChunk)
        {
            var lineCount = Math.Min(LinesPerChunk, lines.Length - startIndex);
            var endLine = startIndex + lineCount;
            var chunkContent = string.Join("\n", lines, startIndex, lineCount);
            if (endLine < lines.Length || endsWithNewLine)
            {
                chunkContent += "\n";
            }

            chunks.Add(new CodeChunk(
                repositoryFileId,
                chunks.Count,
                startIndex + 1,
                endLine,
                chunkContent));
        }

        return chunks;
    }

    private static GitHubRepositoryClientException InvalidGitHubResponse(
        string message,
        Exception? innerException = null) =>
        new(
            GitHubRepositoryClientError.InvalidResponse,
            message,
            innerException: innerException);

    private static GitHubRepositoryClientException FileLimitError(
        GitHubRepositoryClientError error,
        string message) =>
        new(error, message);

    private async Task PersistTerminalStateBeforeTransactionAsync(
        AnalysisSession session,
        Exception originalException,
        CancellationToken cancellationToken)
    {
        try
        {
            MarkTerminal(session, IsCallerCancellation(originalException, cancellationToken));
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception persistenceException)
        {
            ThrowWithPersistenceFailure(originalException, persistenceException, cancellationToken);
        }
    }

    private async Task PersistTerminalStateAfterRollbackAsync(
        Guid sessionId,
        Exception originalException,
        CancellationToken cancellationToken)
    {
        try
        {
            var session = await sessionRepository.GetByIdAsync(sessionId, CancellationToken.None)
                ?? throw new InvalidOperationException("The analysis session could not be reloaded after rollback.");

            if (session.Status == AnalysisSessionStatus.Running)
            {
                MarkTerminal(session, IsCallerCancellation(originalException, cancellationToken));
                sessionRepository.Update(session);
                await unitOfWork.SaveChangesAsync(CancellationToken.None);
            }
        }
        catch (Exception persistenceException)
        {
            ThrowWithPersistenceFailure(originalException, persistenceException, cancellationToken);
        }
    }

    private static void MarkTerminal(AnalysisSession session, bool cancelled)
    {
        var completedAtUtc = DateTimeOffset.UtcNow;
        if (cancelled)
        {
            session.MarkCancelled(completedAtUtc);
        }
        else
        {
            session.MarkFailed(completedAtUtc);
        }
    }

    private static bool IsCallerCancellation(Exception exception, CancellationToken cancellationToken) =>
        FindCallerCancellation(exception, cancellationToken) is not null;

    private static OperationCanceledException? FindCallerCancellation(
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        if (exception is OperationCanceledException cancellationException)
        {
            return cancellationException;
        }

        if (exception is AggregateException aggregateException)
        {
            foreach (var innerException in aggregateException.InnerExceptions)
            {
                var cancellation = FindCallerCancellation(innerException, cancellationToken);
                if (cancellation is not null)
                {
                    return cancellation;
                }
            }

            return null;
        }

        return exception.InnerException is null
            ? null
            : FindCallerCancellation(exception.InnerException, cancellationToken);
    }

    private static void RethrowPreservingCallerCancellation(
        Exception exception,
        CancellationToken cancellationToken)
    {
        var callerCancellation = FindCallerCancellation(exception, cancellationToken);
        if (callerCancellation is not null)
        {
            if (!ReferenceEquals(callerCancellation, exception))
            {
                callerCancellation.Data["CodeMentor.RepositoryIngestion.TransactionFailure"] = exception;
            }

            ExceptionDispatchInfo.Capture(callerCancellation).Throw();
        }

        ExceptionDispatchInfo.Capture(exception).Throw();
    }

    private static void ThrowWithPersistenceFailure(
        Exception originalException,
        Exception persistenceException,
        CancellationToken cancellationToken)
    {
        if (IsCallerCancellation(originalException, cancellationToken))
        {
            originalException.Data[SessionPersistenceFailureDataKey] = persistenceException;
            return;
        }

        throw new AggregateException(
            "Repository ingestion failed and its terminal analysis-session state could not be persisted.",
            originalException,
            persistenceException);
    }

    private sealed record IngestedFile(RepositoryFile File, IReadOnlyList<CodeChunk> Chunks);
}
