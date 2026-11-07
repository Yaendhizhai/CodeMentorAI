using System.ComponentModel.DataAnnotations;
using CodeMentor.Application.Abstractions.Persistence;
using CodeMentor.Application.GitHub;
using CodeMentor.Application.RepositoryIngestion;
using CodeMentor.Core.Entities;
using CodeMentor.Core.Enums;

namespace CodeMentor.Tests;

public sealed class RepositoryIngestionServiceTests
{
    [Fact]
    public async Task IngestAsync_PersistsSelectedFilesChunksAndCompletedSession()
    {
        var source = string.Join("\n", Enumerable.Range(1, 201).Select(index => $"line {index}"));
        var tree = CreateTree(
            new GitHubRepositoryTreeEntry("src/Example.cs", GitHubRepositoryTreeEntryType.Blob, source.Length, Sha('a')),
            new GitHubRepositoryTreeEntry("docs/empty.md", GitHubRepositoryTreeEntryType.Blob, 0, Sha('b')),
            new GitHubRepositoryTreeEntry("config/.env", GitHubRepositoryTreeEntryType.Blob, 4, Sha('c')),
            new GitHubRepositoryTreeEntry("src", GitHubRepositoryTreeEntryType.Tree, null, Sha('d')));
        var github = new FakeGitHubRepositoryClient
        {
            Tree = tree,
            Contents =
            [
                new GitHubRepositoryFileContent("src/Example.cs", Sha('a'), source.Length, source),
                new GitHubRepositoryFileContent("docs/empty.md", Sha('b'), 0, string.Empty)
            ]
        };
        var fixture = new Fixture(github);

        var response = await fixture.Service.IngestAsync(fixture.Project.Id);

        Assert.Equal(fixture.Project.Id, response.RepositoryProjectId);
        Assert.Equal(tree.Sha, response.TreeSha);
        Assert.Equal(2, response.FileCount);
        Assert.Equal(2, response.ChunkCount);
        Assert.Equal(2, fixture.Files.Added.Count);
        Assert.All(fixture.Files.Added, file => Assert.Equal(fixture.Project.Id, file.ProjectId));
        Assert.Equal(Sha('a'), fixture.Files.Added.Single(file => file.RelativePath == "src/Example.cs").ContentHash);
        var emptyFile = fixture.Files.Added.Single(file => file.RelativePath == "docs/empty.md");
        Assert.DoesNotContain(fixture.Chunks.Added, chunk => chunk.RepositoryFileId == emptyFile.Id);
        Assert.Equal(
            new[] { "docs/empty.md", "src/Example.cs" },
            github.SelectedPaths);
        Assert.Equal(2, fixture.Chunks.Added.Count);
        Assert.Equal((0, 1, 200), (
            fixture.Chunks.Added[0].ChunkIndex,
            fixture.Chunks.Added[0].StartLine,
            fixture.Chunks.Added[0].EndLine));
        Assert.Equal((1, 201, 201), (
            fixture.Chunks.Added[1].ChunkIndex,
            fixture.Chunks.Added[1].StartLine,
            fixture.Chunks.Added[1].EndLine));
        Assert.Equal(AnalysisSessionStatus.Completed, fixture.Sessions.PersistedStatus);
        Assert.NotNull(fixture.Sessions.PersistedCompletedAtUtc);
        Assert.Equal(TimeSpan.Zero, response.CompletedAtUtc.Offset);
    }

    [Fact]
    public async Task IngestAsync_PreservesWhitespaceOnlySourceContentAsAChunk()
    {
        var sha = Sha('e');
        var github = new FakeGitHubRepositoryClient
        {
            Tree = CreateTree(
                new GitHubRepositoryTreeEntry("src/blank.cs", GitHubRepositoryTreeEntryType.Blob, 1, sha)),
            Contents = [new GitHubRepositoryFileContent("src/blank.cs", sha, 1, "\n")]
        };
        var fixture = new Fixture(github);

        await fixture.Service.IngestAsync(fixture.Project.Id);

        var chunk = Assert.Single(fixture.Chunks.Added);
        Assert.Equal("\n", chunk.Content);
        Assert.Equal((1, 1), (chunk.StartLine, chunk.EndLine));
    }

    [Fact]
    public async Task IngestAsync_PersistsFailedSessionWhenGitHubVerificationFails()
    {
        var failure = new GitHubRepositoryClientException(
            GitHubRepositoryClientError.NotFound,
            "Repository not found.");
        var fixture = new Fixture(new FakeGitHubRepositoryClient { TreeException = failure });

        var actual = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => fixture.Service.IngestAsync(fixture.Project.Id));

        Assert.Same(failure, actual);
        Assert.Equal(AnalysisSessionStatus.Failed, fixture.Sessions.PersistedStatus);
        Assert.Empty(fixture.Files.Added);
        Assert.Empty(fixture.Chunks.Added);
    }

    [Fact]
    public async Task IngestAsync_RollsBackAllFileAndChunkWritesAndPersistsFailedSession()
    {
        var fixture = new Fixture(new FakeGitHubRepositoryClient());
        fixture.UnitOfWork.FailTransactionSave = true;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.IngestAsync(fixture.Project.Id));

        Assert.Equal("transaction save failed", exception.Message);
        Assert.Empty(fixture.Files.Added);
        Assert.Empty(fixture.Chunks.Added);
        Assert.Equal(AnalysisSessionStatus.Failed, fixture.Sessions.PersistedStatus);
    }

    [Fact]
    public async Task IngestAsync_ExposesRollbackFailureWithoutLosingTransactionFailure()
    {
        var fixture = new Fixture(new FakeGitHubRepositoryClient());
        fixture.UnitOfWork.FailTransactionSave = true;
        fixture.UnitOfWork.RollbackException = new InvalidOperationException("rollback failed");

        var exception = await Assert.ThrowsAsync<AggregateException>(
            () => fixture.Service.IngestAsync(fixture.Project.Id));

        Assert.Contains(exception.InnerExceptions, inner => inner.Message == "transaction save failed");
        Assert.Contains(exception.InnerExceptions, inner => inner.Message == "rollback failed");
        Assert.Equal(AnalysisSessionStatus.Failed, fixture.Sessions.PersistedStatus);
    }

    [Fact]
    public async Task IngestAsync_PreservesIngestionAndTerminalPersistenceFailures()
    {
        var originalFailure = new GitHubRepositoryClientException(
            GitHubRepositoryClientError.NotFound,
            "Repository not found.");
        var fixture = new Fixture(new FakeGitHubRepositoryClient { TreeException = originalFailure });
        fixture.UnitOfWork.FailTerminalSave = true;

        var exception = await Assert.ThrowsAsync<AggregateException>(
            () => fixture.Service.IngestAsync(fixture.Project.Id));

        Assert.Contains(exception.InnerExceptions, inner => ReferenceEquals(inner, originalFailure));
        Assert.Contains(exception.InnerExceptions, inner => inner.Message == "terminal save failed");
        Assert.Equal(AnalysisSessionStatus.Running, fixture.Sessions.PersistedStatus);
    }

    [Fact]
    public async Task IngestAsync_PersistsCancelledSessionBeforePropagatingCallerCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        var github = new FakeGitHubRepositoryClient
        {
            OnGetTree = () =>
            {
                cancellationSource.Cancel();
                throw new OperationCanceledException(cancellationSource.Token);
            }
        };
        var fixture = new Fixture(github);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Service.IngestAsync(fixture.Project.Id, cancellationSource.Token));

        Assert.Equal(cancellationSource.Token, exception.CancellationToken);
        Assert.Equal(AnalysisSessionStatus.Cancelled, fixture.Sessions.PersistedStatus);
        Assert.Equal(CancellationToken.None, fixture.UnitOfWork.LastSaveCancellationToken);
    }

    [Fact]
    public async Task IngestAsync_PersistsCancellationAndSurfacesRollbackFailure()
    {
        using var cancellationSource = new CancellationTokenSource();
        var fixture = new Fixture(new FakeGitHubRepositoryClient());
        var rollbackFailure = new InvalidOperationException("rollback failed");
        fixture.UnitOfWork.CancelTransaction = cancellationSource;
        fixture.UnitOfWork.RollbackException = rollbackFailure;

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Service.IngestAsync(fixture.Project.Id, cancellationSource.Token));

        Assert.Equal(AnalysisSessionStatus.Cancelled, fixture.Sessions.PersistedStatus);
        Assert.Contains(
            "CodeMentor.RepositoryIngestion.TransactionFailure",
            exception.Data.Keys.Cast<string>());
        var transactionFailure = Assert.IsType<AggregateException>(
            exception.Data["CodeMentor.RepositoryIngestion.TransactionFailure"]);
        Assert.Contains(transactionFailure.InnerExceptions, inner => ReferenceEquals(inner, rollbackFailure));
    }

    [Fact]
    public async Task IngestAsync_RejectsProjectsThatAlreadyHaveFiles()
    {
        var fixture = new Fixture(new FakeGitHubRepositoryClient());
        fixture.Files.HasExistingFiles = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.IngestAsync(fixture.Project.Id));

        Assert.Equal(0, fixture.Sessions.AddCount);
        Assert.Equal(0, fixture.GitHub.TreeCallCount);
    }

    [Fact]
    public async Task IngestAsync_RejectsEligibleFilesOverConfiguredPerFileLimitBeforeDownloading()
    {
        var limits = new GitHubRepositoryFileContentLimits(maximumFileBytes: 4);
        var github = new FakeGitHubRepositoryClient();
        var fixture = new Fixture(github, contentLimits: limits);

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => fixture.Service.IngestAsync(fixture.Project.Id));

        Assert.Equal(GitHubRepositoryClientError.FileTooLarge, exception.Error);
        Assert.Equal(0, github.FileCallCount);
        Assert.Equal(AnalysisSessionStatus.Failed, fixture.Sessions.PersistedStatus);
    }

    [Fact]
    public async Task IngestAsync_RejectsFileCountAndAggregateLimitsBeforeDownloading()
    {
        var tree = CreateTree(
            new GitHubRepositoryTreeEntry("src/one.cs", GitHubRepositoryTreeEntryType.Blob, 1, Sha('1')),
            new GitHubRepositoryTreeEntry("src/two.cs", GitHubRepositoryTreeEntryType.Blob, 1, Sha('2')));
        var countLimitedGithub = new FakeGitHubRepositoryClient { Tree = tree };
        var countLimited = new Fixture(
            countLimitedGithub,
            contentLimits: new GitHubRepositoryFileContentLimits(maximumSelectedFiles: 1));

        var countException = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => countLimited.Service.IngestAsync(countLimited.Project.Id));

        Assert.Equal(GitHubRepositoryClientError.TooManyFiles, countException.Error);
        Assert.Equal(0, countLimitedGithub.FileCallCount);

        var aggregateLimitedGithub = new FakeGitHubRepositoryClient { Tree = tree };
        var aggregateLimited = new Fixture(
            aggregateLimitedGithub,
            contentLimits: new GitHubRepositoryFileContentLimits(maximumTotalBytes: 1));

        var aggregateException = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => aggregateLimited.Service.IngestAsync(aggregateLimited.Project.Id));

        Assert.Equal(GitHubRepositoryClientError.TotalDownloadTooLarge, aggregateException.Error);
        Assert.Equal(0, aggregateLimitedGithub.FileCallCount);
        Assert.Empty(aggregateLimited.Files.Added);
    }

    [Fact]
    public async Task IngestAsync_RejectsInvalidRepositoryUrlBeforeCreatingSession()
    {
        var fixture = new Fixture(new FakeGitHubRepositoryClient(), "https://example.com/owner/repository");

        await Assert.ThrowsAsync<ValidationException>(
            () => fixture.Service.IngestAsync(fixture.Project.Id));

        Assert.Equal(0, fixture.Sessions.AddCount);
        Assert.Equal(0, fixture.GitHub.TreeCallCount);
    }

    private static GitHubRepositoryTree CreateTree(params GitHubRepositoryTreeEntry[] entries) =>
        new("example", "repository", Sha('f'), entries);

    private static string Sha(char character) => new(character, 40);

    private sealed class Fixture
    {
        public Fixture(
            FakeGitHubRepositoryClient github,
            string? repositoryUrl = "https://github.com/example/repository",
            GitHubRepositoryFileContentLimits? contentLimits = null)
        {
            Project = new RepositoryProject("Test project", repositoryUrl);
            Projects = new FakeRepositoryProjectRepository(Project);
            Files = new FakeRepositoryFileRepository();
            Chunks = new FakeCodeChunkRepository();
            Sessions = new FakeAnalysisSessionRepository();
            UnitOfWork = new FakeUnitOfWork(Sessions, Files, Chunks);
            GitHub = github;
            Service = new RepositoryIngestionService(
                Projects,
                Files,
                Chunks,
                Sessions,
                UnitOfWork,
                GitHub,
                contentLimits ?? new GitHubRepositoryFileContentLimits(),
                new GitHubRepositoryFileSelectionPolicy());
        }

        public RepositoryProject Project { get; }
        public FakeRepositoryProjectRepository Projects { get; }
        public FakeRepositoryFileRepository Files { get; }
        public FakeCodeChunkRepository Chunks { get; }
        public FakeAnalysisSessionRepository Sessions { get; }
        public FakeUnitOfWork UnitOfWork { get; }
        public FakeGitHubRepositoryClient GitHub { get; }
        public IRepositoryIngestionService Service { get; }
    }

    private sealed class FakeGitHubRepositoryClient : IGitHubRepositoryClient
    {
        public GitHubRepositoryTree Tree { get; init; } = CreateTree(
            new GitHubRepositoryTreeEntry(
                "src/file.cs",
                GitHubRepositoryTreeEntryType.Blob,
                11,
                Sha('1')));

        public IReadOnlyList<GitHubRepositoryFileContent> Contents { get; init; } =
        [
            new GitHubRepositoryFileContent("src/file.cs", Sha('1'), 11, "hello world")
        ];

        public Exception? TreeException { get; init; }
        public Action? OnGetTree { get; init; }
        public int TreeCallCount { get; private set; }
        public int FileCallCount { get; private set; }
        public string[] SelectedPaths { get; private set; } = [];

        public Task<GitHubRepositoryMetadata> GetPublicRepositoryAsync(
            GitHubRepositoryReference repository,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubRepositoryMetadata(
                repository.Owner,
                repository.RepositoryName,
                repository.CanonicalUrl,
                "main",
                "public"));

        public Task<GitHubRepositoryTree> GetRepositoryTreeAsync(
            GitHubRepositoryReference repository,
            CancellationToken cancellationToken = default)
        {
            TreeCallCount++;
            OnGetTree?.Invoke();
            return TreeException is null
                ? Task.FromResult(Tree)
                : Task.FromException<GitHubRepositoryTree>(TreeException);
        }

        public Task<IReadOnlyList<GitHubRepositoryFileContent>> GetRepositoryFilesAsync(
            GitHubRepositoryReference repository,
            GitHubRepositoryTree tree,
            IReadOnlyCollection<string> selectedPaths,
            CancellationToken cancellationToken = default)
        {
            FileCallCount++;
            SelectedPaths = selectedPaths.ToArray();
            return Task.FromResult(Contents);
        }
    }

    private sealed class FakeRepositoryProjectRepository(RepositoryProject project) : IRepositoryProjectRepository
    {
        public Task<RepositoryProject?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(id == project.Id ? project : null);

        public Task<IReadOnlyList<RepositoryProject>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RepositoryProject>>([project]);

        public Task AddAsync(RepositoryProject repositoryProject, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeRepositoryFileRepository : IRepositoryFileRepository
    {
        public bool HasExistingFiles { get; set; }
        public List<RepositoryFile> Added { get; } = [];

        public Task<bool> HasAnyByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(HasExistingFiles || Added.Count > 0);

        public Task<RepositoryFile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Added.SingleOrDefault(file => file.Id == id));

        public Task<IReadOnlyList<RepositoryFile>> GetByProjectIdAsync(
            Guid projectId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RepositoryFile>>(Added.Where(file => file.ProjectId == projectId).ToArray());

        public Task AddAsync(RepositoryFile file, CancellationToken cancellationToken = default)
        {
            Added.Add(file);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCodeChunkRepository : ICodeChunkRepository
    {
        public List<CodeChunk> Added { get; } = [];

        public Task<IReadOnlyList<CodeChunk>> GetByRepositoryFileIdAsync(
            Guid repositoryFileId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CodeChunk>>(
                Added.Where(chunk => chunk.RepositoryFileId == repositoryFileId).ToArray());

        public Task AddAsync(CodeChunk chunk, CancellationToken cancellationToken = default)
        {
            Added.Add(chunk);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAnalysisSessionRepository : IAnalysisSessionRepository
    {
        private SessionSnapshot? _persisted;

        public AnalysisSession? TrackedSession { get; private set; }
        public int AddCount { get; private set; }
        public AnalysisSessionStatus? PersistedStatus => _persisted?.Status;
        public DateTimeOffset? PersistedCompletedAtUtc => _persisted?.CompletedAtUtc;
        public SessionSnapshot? Snapshot => _persisted;

        public Task<AnalysisSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_persisted?.Id == id ? CreateCopy(_persisted) : null);

        public Task<IReadOnlyList<AnalysisSession>> GetByProjectIdAsync(
            Guid projectId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AnalysisSession>>([]);

        public Task AddAsync(AnalysisSession session, CancellationToken cancellationToken = default)
        {
            TrackedSession = session;
            AddCount++;
            return Task.CompletedTask;
        }

        public void Update(AnalysisSession session) => TrackedSession = session;

        public void Persist()
        {
            if (TrackedSession is null)
            {
                throw new InvalidOperationException("No analysis session is tracked.");
            }

            _persisted = SessionSnapshot.From(TrackedSession);
        }

        public void Restore(SessionSnapshot? snapshot) => _persisted = snapshot;

        private static AnalysisSession CreateCopy(SessionSnapshot snapshot)
        {
            var copy = new AnalysisSession(snapshot.ProjectId);
            var startedAt = DateTimeOffset.UtcNow;
            copy.MarkRunning(startedAt);

            if (snapshot.Status == AnalysisSessionStatus.Completed)
            {
                copy.MarkCompleted(startedAt.AddTicks(1));
            }
            else if (snapshot.Status == AnalysisSessionStatus.Failed)
            {
                copy.MarkFailed(startedAt.AddTicks(1));
            }
            else if (snapshot.Status == AnalysisSessionStatus.Cancelled)
            {
                copy.MarkCancelled(startedAt.AddTicks(1));
            }

            return copy;
        }
    }

    private sealed record SessionSnapshot(
        Guid Id,
        Guid ProjectId,
        AnalysisSessionStatus Status,
        DateTimeOffset? CompletedAtUtc)
    {
        public static SessionSnapshot From(AnalysisSession session) =>
            new(session.Id, session.ProjectId, session.Status, session.CompletedAtUtc);
    }

    private sealed class FakeUnitOfWork(
        FakeAnalysisSessionRepository sessions,
        FakeRepositoryFileRepository files,
        FakeCodeChunkRepository chunks) : IUnitOfWork
    {
        private bool _inTransaction;

        public bool FailTransactionSave { get; set; }
        public bool FailTerminalSave { get; set; }
        public CancellationTokenSource? CancelTransaction { get; set; }
        public Exception? RollbackException { get; set; }
        public CancellationToken LastSaveCancellationToken { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            LastSaveCancellationToken = cancellationToken;
            if (_inTransaction && CancelTransaction is not null)
            {
                CancelTransaction.Cancel();
                throw new OperationCanceledException(CancelTransaction.Token);
            }

            if (FailTransactionSave && _inTransaction)
            {
                FailTransactionSave = false;
                throw new InvalidOperationException("transaction save failed");
            }

            if (FailTerminalSave &&
                sessions.TrackedSession?.Status is AnalysisSessionStatus.Failed or AnalysisSessionStatus.Cancelled)
            {
                FailTerminalSave = false;
                throw new InvalidOperationException("terminal save failed");
            }

            sessions.Persist();
            return Task.FromResult(1);
        }

        public async Task ExecuteInTransactionAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken = default)
        {
            var filesBefore = files.Added.Count;
            var chunksBefore = chunks.Added.Count;
            var snapshotBefore = sessions.Snapshot;
            _inTransaction = true;

            try
            {
                await operation(cancellationToken);
            }
            catch (Exception exception)
            {
                files.Added.RemoveRange(filesBefore, files.Added.Count - filesBefore);
                chunks.Added.RemoveRange(chunksBefore, chunks.Added.Count - chunksBefore);
                sessions.Restore(snapshotBefore);
                if (RollbackException is not null)
                {
                    throw new AggregateException(exception, RollbackException);
                }

                throw;
            }
            finally
            {
                _inTransaction = false;
            }
        }
    }
}
