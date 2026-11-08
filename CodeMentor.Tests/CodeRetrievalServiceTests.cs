using CodeMentor.Application.Abstractions.Persistence;
using CodeMentor.Application.CodeRetrieval;
using CodeMentor.Core.Entities;

namespace CodeMentor.Tests;

public sealed class CodeRetrievalServiceTests
{
    [Fact]
    public async Task SearchAsync_NormalizesAndDeduplicatesTermsAndUsesDefaultLimit()
    {
        var repository = new FakeCodeChunkRepository();
        var service = new CodeRetrievalService(repository);
        var projectId = Guid.NewGuid();

        var results = await service.SearchAsync(projectId, " Foo, foo! BAR_42 -- !!! ");

        Assert.Empty(results);
        Assert.Equal(projectId, repository.ProjectId);
        Assert.Equal(new[] { "foo", "bar", "42" }, repository.Terms);
        Assert.Equal(CodeRetrievalService.DefaultResultLimit, repository.Limit);
    }

    [Fact]
    public async Task SearchAsync_PassesCustomLimitCancellationAndResultsThrough()
    {
        using var cancellationSource = new CancellationTokenSource();
        var expected =
            new CodeChunkSearchResult("src/test.cs", 2, 401, 600, "class Widget {}", 2);
        var repository = new FakeCodeChunkRepository { Results = [expected] };
        var service = new CodeRetrievalService(repository);
        var projectId = Guid.NewGuid();

        var results = await service.SearchAsync(
            projectId,
            "Widget widget class",
            7,
            cancellationSource.Token);

        Assert.Equal(new[] { expected }, results);
        Assert.Equal(projectId, repository.ProjectId);
        Assert.Equal(new[] { "widget", "class" }, repository.Terms);
        Assert.Equal(7, repository.Limit);
        Assert.Equal(cancellationSource.Token, repository.CancellationToken);
    }

    [Fact]
    public async Task SearchAsync_RejectsEmptyProjectId()
    {
        var service = new CodeRetrievalService(new FakeCodeChunkRepository());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SearchAsync(Guid.Empty, "class"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \t\r\n")]
    [InlineData("!@#$%^&*()")]
    public async Task SearchAsync_RejectsBlankOrNonSearchableQueries(string query)
    {
        var service = new CodeRetrievalService(new FakeCodeChunkRepository());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SearchAsync(Guid.NewGuid(), query));
    }

    [Fact]
    public async Task SearchAsync_RejectsQueryLongerThanMaximum()
    {
        var service = new CodeRetrievalService(new FakeCodeChunkRepository());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SearchAsync(
                Guid.NewGuid(),
                new string('a', CodeRetrievalService.MaximumQueryLength + 1)));
    }

    [Fact]
    public async Task SearchAsync_RejectsMoreThanMaximumDistinctTerms()
    {
        var query = string.Join(
            ' ',
            Enumerable.Range(0, CodeRetrievalService.MaximumDistinctTerms + 1)
                .Select(index => $"term{index}"));
        var service = new CodeRetrievalService(new FakeCodeChunkRepository());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SearchAsync(Guid.NewGuid(), query));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task SearchAsync_RejectsInvalidResultLimit(int limit)
    {
        var service = new CodeRetrievalService(new FakeCodeChunkRepository());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.SearchAsync(Guid.NewGuid(), "class", limit));
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmptyWhenRepositoryHasNoMatches()
    {
        var repository = new FakeCodeChunkRepository();
        var service = new CodeRetrievalService(repository);

        var results = await service.SearchAsync(Guid.NewGuid(), "missing-symbol");

        Assert.Empty(results);
        Assert.Equal(new[] { "missing", "symbol" }, repository.Terms);
    }

    private sealed class FakeCodeChunkRepository : ICodeChunkRepository
    {
        public Guid ProjectId { get; private set; }
        public IReadOnlyList<string> Terms { get; private set; } = [];
        public int Limit { get; private set; }
        public CancellationToken CancellationToken { get; private set; }
        public IReadOnlyList<CodeChunkSearchResult> Results { get; init; } = [];

        public Task<IReadOnlyList<CodeChunk>> GetByRepositoryFileIdAsync(
            Guid repositoryFileId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CodeChunk>>([]);

        public Task<IReadOnlyList<CodeChunkSearchResult>> SearchAsync(
            Guid projectId,
            IReadOnlyList<string> normalizedTerms,
            int limit,
            CancellationToken cancellationToken = default)
        {
            ProjectId = projectId;
            Terms = normalizedTerms.ToArray();
            Limit = limit;
            CancellationToken = cancellationToken;
            return Task.FromResult(Results);
        }

        public Task AddAsync(CodeChunk chunk, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
