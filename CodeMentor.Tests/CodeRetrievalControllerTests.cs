using CodeMentor.API.Controllers;
using CodeMentor.Application.CodeRetrieval;
using CodeMentor.Application.RepositoryIngestion;
using CodeMentor.Application.RepositoryProjects;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CodeMentor.Tests;

public sealed class CodeRetrievalControllerTests
{
    [Fact]
    public async Task SearchCode_ReturnsMatchingResultsAndForwardsRequestValues()
    {
        var projectId = Guid.NewGuid();
        var expected = new[]
        {
            new CodeChunkSearchResult("src/Widget.cs", 0, 1, 15, "public class Widget {}", 2)
        };
        var projectService = new FakeRepositoryProjectService(projectId);
        var retrievalService = new FakeCodeRetrievalService { Results = expected };
        var controller = CreateController(projectService, retrievalService);
        using var cancellationSource = new CancellationTokenSource();

        var result = await controller.SearchCode(
            projectId,
            new CodeSearchRequest { Query = "class Widget", Limit = 7 },
            cancellationSource.Token);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);
        Assert.Equal(expected, okResult.Value);
        Assert.Equal(projectId, retrievalService.ProjectId);
        Assert.Equal("class Widget", retrievalService.Query);
        Assert.Equal(7, retrievalService.Limit);
        Assert.Equal(cancellationSource.Token, retrievalService.CancellationToken);
    }

    [Fact]
    public async Task SearchCode_ReturnsEmptyCollectionWhenThereAreNoMatches()
    {
        var projectId = Guid.NewGuid();
        var retrievalService = new FakeCodeRetrievalService();
        var controller = CreateController(
            new FakeRepositoryProjectService(projectId),
            retrievalService);

        var result = await controller.SearchCode(
            projectId,
            new CodeSearchRequest { Query = "missing-symbol" },
            CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<CodeChunkSearchResult>>(okResult.Value));
        Assert.Equal(10, retrievalService.Limit);
    }

    [Fact]
    public async Task SearchCode_ReturnsNotFoundWithoutSearchingWhenProjectDoesNotExist()
    {
        var retrievalService = new FakeCodeRetrievalService();
        var controller = CreateController(
            new FakeRepositoryProjectService(null),
            retrievalService);

        var result = await controller.SearchCode(
            Guid.NewGuid(),
            new CodeSearchRequest { Query = "class Widget" },
            CancellationToken.None);

        var problemResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, problemResult.StatusCode);
        Assert.Equal(0, retrievalService.CallCount);
    }

    [Fact]
    public async Task SearchCode_ReturnsSafeBadRequestForInvalidQuery()
    {
        var projectId = Guid.NewGuid();
        var controller = CreateController(
            new FakeRepositoryProjectService(projectId),
            new FakeCodeRetrievalService
            {
                ExceptionToThrow = new ArgumentException("internal query validation detail")
            });

        var result = await controller.SearchCode(
            projectId,
            new CodeSearchRequest { Query = "!!!" },
            CancellationToken.None);

        AssertSafeBadRequest(result.Result, "internal query validation detail");
    }

    [Fact]
    public async Task SearchCode_ReturnsSafeBadRequestForInvalidLimit()
    {
        var projectId = Guid.NewGuid();
        var controller = CreateController(
            new FakeRepositoryProjectService(projectId),
            new FakeCodeRetrievalService
            {
                ExceptionToThrow = new ArgumentOutOfRangeException("limit", "internal limit validation detail")
            });

        var result = await controller.SearchCode(
            projectId,
            new CodeSearchRequest { Query = "class Widget", Limit = 101 },
            CancellationToken.None);

        AssertSafeBadRequest(result.Result, "internal limit validation detail");
    }

    [Fact]
    public async Task SearchCode_PropagatesCancellation()
    {
        var projectId = Guid.NewGuid();
        using var cancellationSource = new CancellationTokenSource();
        var cancellation = new OperationCanceledException(cancellationSource.Token);
        var controller = CreateController(
            new FakeRepositoryProjectService(projectId),
            new FakeCodeRetrievalService { ExceptionToThrow = cancellation });

        var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => controller.SearchCode(
                projectId,
                new CodeSearchRequest { Query = "class Widget" },
                cancellationSource.Token));

        Assert.Same(cancellation, actual);
    }

    private static RepositoryProjectsController CreateController(
        IRepositoryProjectService projectService,
        ICodeRetrievalService retrievalService) =>
        new(projectService, new FakeRepositoryIngestionService(), retrievalService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

    private static void AssertSafeBadRequest(IActionResult? actionResult, string sensitiveDetail)
    {
        var problemResult = Assert.IsType<ObjectResult>(actionResult);
        var problem = Assert.IsType<ProblemDetails>(problemResult.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, problemResult.StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.DoesNotContain(sensitiveDetail, problem.Detail);
    }

    private sealed class FakeRepositoryProjectService(Guid? existingProjectId) : IRepositoryProjectService
    {
        public Task<RepositoryProjectResponse> RegisterAsync(
            RegisterRepositoryProjectRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RepositoryProjectResponse?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<RepositoryProjectResponse?>(
                existingProjectId == id
                    ? new RepositoryProjectResponse(id, "Test project", null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
                    : null);

        public Task<IReadOnlyList<RepositoryProjectResponse>> GetAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RepositoryProjectResponse>>([]);
    }

    private sealed class FakeRepositoryIngestionService : IRepositoryIngestionService
    {
        public Task<RepositoryIngestionResponse> IngestAsync(
            Guid repositoryProjectId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

internal sealed class FakeCodeRetrievalService : ICodeRetrievalService
{
    public IReadOnlyList<CodeChunkSearchResult> Results { get; init; } = [];

    public Exception? ExceptionToThrow { get; init; }

    public Guid ProjectId { get; private set; }

    public string? Query { get; private set; }

    public int Limit { get; private set; }

    public CancellationToken CancellationToken { get; private set; }

    public int CallCount { get; private set; }

    public Task<IReadOnlyList<CodeChunkSearchResult>> SearchAsync(
        Guid projectId,
        string query,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        ProjectId = projectId;
        Query = query;
        Limit = limit;
        CancellationToken = cancellationToken;
        return ExceptionToThrow is null
            ? Task.FromResult(Results)
            : Task.FromException<IReadOnlyList<CodeChunkSearchResult>>(ExceptionToThrow);
    }
}
