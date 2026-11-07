using CodeMentor.Application;
using CodeMentor.Application.GitHub;
using CodeMentor.Application.RepositoryIngestion;
using CodeMentor.Core.Entities;
using CodeMentor.Core.Enums;
using CodeMentor.Infrastructure;
using CodeMentor.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Security.Cryptography;
using System.Text;
using Xunit.Sdk;

namespace CodeMentor.Tests;

public sealed class RepositoryIngestionSqlServerIntegrationTests
{
    private const string AllowedDatabaseName = "CodeMentorAI_IntegrationTests";
    private const string AllowedDataSource = "localhost";
    private const string SourceFilePath = "src/IntegrationSample.cs";
    private const string ReadmeFilePath = "docs/IntegrationFixture.md";
    private const string TreeSha = "ffffffffffffffffffffffffffffffffffffffff";
    private const string SourceFileContent =
        "namespace Integration;\n\npublic sealed class Sample { }\n";
    private const string ReadmeFileContent = "Repository ingestion integration fixture.\n";
    private static readonly string SourceFileSha = ComputeGitBlobSha(SourceFileContent);
    private static readonly string ReadmeFileSha = ComputeGitBlobSha(ReadmeFileContent);

    [SqlServerIntegrationFact]
    public async Task IngestAsync_PersistsFilesChunksAndCompletedSessionInConfiguredSqlServer()
    {
        var connectionString = GetRequiredEnvironmentVariable("CODEMENTOR_SQL_INTEGRATION_CONNECTION_STRING");
        var projectId = Guid.Parse(GetRequiredEnvironmentVariable("CODEMENTOR_SQL_INTEGRATION_PROJECT_ID"));
        var connectionStringBuilder = new SqlConnectionStringBuilder(connectionString);
        if (!string.Equals(
                connectionStringBuilder.InitialCatalog,
                AllowedDatabaseName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The opt-in integration test only permits database '{AllowedDatabaseName}'.");
        }

        if (!string.Equals(
                connectionStringBuilder.DataSource,
                AllowedDataSource,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The opt-in integration test only permits SQL Server DataSource '{AllowedDataSource}'.");
        }

        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure(connectionString);
        var fakeGitHubClient = new DeterministicGitHubRepositoryClient();
        services.RemoveAll<IGitHubRepositoryClient>();
        services.AddSingleton<IGitHubRepositoryClient>(fakeGitHubClient);

        await using var serviceProvider = services.BuildServiceProvider();
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CodeMentorDbContext>();
        if (!await dbContext.Database.CanConnectAsync())
        {
            throw new InvalidOperationException("The configured SQL Server integration-test database is unavailable.");
        }

        var project = await dbContext.RepositoryProjects
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == projectId)
            ?? throw new InvalidOperationException("The configured integration-test repository project was not found.");

        if (await dbContext.RepositoryFiles.AnyAsync(file => file.ProjectId == projectId))
        {
            throw new InvalidOperationException(
                "The configured integration-test project already has repository files; ingestion is non-repeatable.");
        }

        if (await dbContext.AnalysisSessions.AnyAsync(session => session.ProjectId == projectId))
        {
            throw new InvalidOperationException(
                "The configured integration-test project already has analysis sessions.");
        }

        var service = scope.ServiceProvider.GetRequiredService<IRepositoryIngestionService>();
        var result = await service.IngestAsync(project.Id);

        Assert.Equal(project.Id, result.RepositoryProjectId);
        Assert.Equal(TreeSha, result.TreeSha);
        Assert.Equal(2, result.FileCount);
        Assert.Equal(2, result.ChunkCount);
        Assert.Equal(1, fakeGitHubClient.TreeRequestCount);
        Assert.Equal(1, fakeGitHubClient.FileRequestCount);
        Assert.Equal(
            new[] { ReadmeFilePath, SourceFilePath },
            fakeGitHubClient.RequestedPaths);

        var persistedFiles = await dbContext.RepositoryFiles
            .AsNoTracking()
            .Where(file => file.ProjectId == project.Id)
            .OrderBy(file => file.RelativePath)
            .ToListAsync();
        var persistedChunks = await dbContext.CodeChunks
            .AsNoTracking()
            .Where(chunk => persistedFiles.Select(file => file.Id).Contains(chunk.RepositoryFileId))
            .OrderBy(chunk => chunk.RepositoryFileId)
            .ThenBy(chunk => chunk.ChunkIndex)
            .ToListAsync();
        var persistedSession = await dbContext.AnalysisSessions
            .AsNoTracking()
            .SingleAsync(session => session.Id == result.AnalysisSessionId);

        Assert.Equal(
            new[]
            {
                new ExpectedFile(ReadmeFilePath, ReadmeFileSha, Encoding.UTF8.GetByteCount(ReadmeFileContent)),
                new ExpectedFile(SourceFilePath, SourceFileSha, Encoding.UTF8.GetByteCount(SourceFileContent))
            },
            persistedFiles
                .Select(file => new ExpectedFile(file.RelativePath, file.ContentHash!, file.SizeInBytes))
                .ToArray());
        Assert.Equal(
            new[]
            {
                new ExpectedChunk(ReadmeFilePath, 0, 1, 1, ReadmeFileContent),
                new ExpectedChunk(SourceFilePath, 0, 1, 3, SourceFileContent)
            },
            persistedChunks
                .OrderBy(chunk =>
                    persistedFiles.Single(file => file.Id == chunk.RepositoryFileId).RelativePath,
                    StringComparer.Ordinal)
                .ThenBy(chunk => chunk.ChunkIndex)
                .Select(chunk => new ExpectedChunk(
                    persistedFiles.Single(file => file.Id == chunk.RepositoryFileId).RelativePath,
                    chunk.ChunkIndex,
                    chunk.StartLine,
                    chunk.EndLine,
                    chunk.Content))
                .ToArray());

        Assert.Equal(project.Id, persistedSession.ProjectId);
        Assert.Equal(AnalysisSessionStatus.Completed, persistedSession.Status);
        Assert.NotNull(persistedSession.CompletedAtUtc);
        Assert.NotNull(persistedSession.StartedAtUtc);
        Assert.Equal(TimeSpan.Zero, persistedSession.CreatedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, persistedSession.StartedAtUtc.Value.Offset);
        Assert.Equal(TimeSpan.Zero, persistedSession.CompletedAtUtc.Value.Offset);
        Assert.True(persistedSession.CreatedAtUtc <= persistedSession.StartedAtUtc.Value);
        Assert.True(persistedSession.StartedAtUtc.Value <= persistedSession.CompletedAtUtc.Value);
        Assert.Equal(persistedSession.CompletedAtUtc.Value, result.CompletedAtUtc);
    }

    private sealed record ExpectedFile(string Path, string Sha, long Size);

    private sealed record ExpectedChunk(
        string Path,
        int ChunkIndex,
        int StartLine,
        int EndLine,
        string Content);

    private static string ComputeGitBlobSha(string content)
    {
        var contentBytes = Encoding.UTF8.GetBytes(content);
        var headerBytes = Encoding.ASCII.GetBytes($"blob {contentBytes.Length}\0");
        var blobBytes = new byte[headerBytes.Length + contentBytes.Length];
        Buffer.BlockCopy(headerBytes, 0, blobBytes, 0, headerBytes.Length);
        Buffer.BlockCopy(contentBytes, 0, blobBytes, headerBytes.Length, contentBytes.Length);
        return Convert.ToHexString(SHA1.HashData(blobBytes)).ToLowerInvariant();
    }

    private sealed class DeterministicGitHubRepositoryClient : IGitHubRepositoryClient
    {
        public int TreeRequestCount { get; private set; }

        public int FileRequestCount { get; private set; }

        public string[] RequestedPaths { get; private set; } = [];

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
            cancellationToken.ThrowIfCancellationRequested();
            TreeRequestCount++;

            return Task.FromResult(new GitHubRepositoryTree(
                repository.Owner,
                repository.RepositoryName,
                TreeSha,
                [
                    new GitHubRepositoryTreeEntry(
                        SourceFilePath,
                        GitHubRepositoryTreeEntryType.Blob,
                        Encoding.UTF8.GetByteCount(SourceFileContent),
                        SourceFileSha),
                    new GitHubRepositoryTreeEntry(
                        ReadmeFilePath,
                        GitHubRepositoryTreeEntryType.Blob,
                        Encoding.UTF8.GetByteCount(ReadmeFileContent),
                        ReadmeFileSha)
                ]));
        }

        public Task<IReadOnlyList<GitHubRepositoryFileContent>> GetRepositoryFilesAsync(
            GitHubRepositoryReference repository,
            GitHubRepositoryTree tree,
            IReadOnlyCollection<string> selectedPaths,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileRequestCount++;
            RequestedPaths = selectedPaths.ToArray();

            var contentsByPath = new Dictionary<string, GitHubRepositoryFileContent>(StringComparer.Ordinal)
            {
                [SourceFilePath] = new(
                    SourceFilePath,
                    SourceFileSha,
                    Encoding.UTF8.GetByteCount(SourceFileContent),
                    SourceFileContent),
                [ReadmeFilePath] = new(
                    ReadmeFilePath,
                    ReadmeFileSha,
                    Encoding.UTF8.GetByteCount(ReadmeFileContent),
                    ReadmeFileContent)
            };

            if (!string.Equals(tree.Owner, repository.Owner, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(tree.RepositoryName, repository.RepositoryName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(tree.Sha, TreeSha, StringComparison.Ordinal) ||
                selectedPaths.Count != contentsByPath.Count ||
                selectedPaths.Any(path => !contentsByPath.ContainsKey(path)))
            {
                throw new InvalidOperationException("The ingestion service requested unexpected GitHub fixture data.");
            }

            return Task.FromResult<IReadOnlyList<GitHubRepositoryFileContent>>(
                selectedPaths.Select(path => contentsByPath[path]).ToArray());
        }
    }

    private static string GetRequiredEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Required integration-test environment variable '{name}' is missing.");

    private sealed class SqlServerIntegrationFactAttribute : FactAttribute
    {
        public SqlServerIntegrationFactAttribute()
        {
            if (!string.Equals(
                    Environment.GetEnvironmentVariable("CODEMENTOR_SQL_INTEGRATION"),
                    "1",
                    StringComparison.Ordinal))
            {
                Skip = "Set CODEMENTOR_SQL_INTEGRATION=1 to enable the SQL Server ingestion integration test.";
            }
        }
    }
}
