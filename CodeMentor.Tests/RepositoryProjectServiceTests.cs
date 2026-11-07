using System.ComponentModel.DataAnnotations;
using CodeMentor.Application.Abstractions.Persistence;
using CodeMentor.Application.GitHub;
using CodeMentor.Application.RepositoryProjects;
using CodeMentor.Core.Entities;
using Microsoft.AspNetCore.Http;
using CodeMentor.API.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace CodeMentor.Tests;

public sealed class RepositoryProjectServiceTests
{
    [Fact]
    public async Task RegisterAsync_AddsProjectAndSavesChanges()
    {
        var repository = new FakeRepositoryProjectRepository();
        var unitOfWork = new FakeUnitOfWork();
        var gitHubClient = new FakeGitHubRepositoryClient();
        var service = new RepositoryProjectService(repository, unitOfWork, gitHubClient);
        var cancellationToken = new CancellationTokenSource().Token;

        var response = await service.RegisterAsync(
            new RegisterRepositoryProjectRequest
            {
                Name = "CodeMentor AI",
                RepositoryUrl = "https://github.com/example/codementor-ai.git",
                Description = "Repository-aware application mentor"
            },
            cancellationToken);

        Assert.NotEqual(Guid.Empty, response.Id);
        Assert.Equal("CodeMentor AI", response.Name);
        Assert.Equal("https://github.com/example/codementor-ai", response.RepositoryUrl);
        Assert.Equal("Repository-aware application mentor", response.Description);
        Assert.Equal(response.CreatedAtUtc, response.UpdatedAtUtc);
        Assert.Equal(response.Id, repository.AddedProject?.Id);
        Assert.Equal(1, unitOfWork.SaveChangesCount);
        Assert.Equal(1, gitHubClient.CallCount);
        Assert.Equal("codementor-ai", gitHubClient.RequestedReference?.RepositoryName);
        Assert.Equal(cancellationToken, repository.LastCancellationToken);
        Assert.Equal(cancellationToken, unitOfWork.LastCancellationToken);
    }

    [Fact]
    public async Task RegisterAsync_WithoutRepositoryUrl_SavesNameOnlyProjectWithoutGitHubRequest()
    {
        var repository = new FakeRepositoryProjectRepository();
        var unitOfWork = new FakeUnitOfWork();
        var gitHubClient = new FakeGitHubRepositoryClient();
        var service = new RepositoryProjectService(repository, unitOfWork, gitHubClient);

        var response = await service.RegisterAsync(new RegisterRepositoryProjectRequest
        {
            Name = "Local project",
            RepositoryUrl = null,
            Description = null
        });

        Assert.Equal("Local project", response.Name);
        Assert.Null(response.RepositoryUrl);
        Assert.Equal(0, gitHubClient.CallCount);
        Assert.Equal(1, unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task RegisterAsync_RejectsInvalidRequestWithoutSaving()
    {
        var repository = new FakeRepositoryProjectRepository();
        var unitOfWork = new FakeUnitOfWork();
        var gitHubClient = new FakeGitHubRepositoryClient();
        var service = new RepositoryProjectService(repository, unitOfWork, gitHubClient);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.RegisterAsync(new RegisterRepositoryProjectRequest
            {
                Name = " ",
                RepositoryUrl = null,
                Description = null
            }));

        Assert.Null(repository.AddedProject);
        Assert.Equal(0, unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task RegisterAsync_RejectsInvalidGitHubUrlWithoutCallingClientOrSaving()
    {
        var repository = new FakeRepositoryProjectRepository();
        var unitOfWork = new FakeUnitOfWork();
        var gitHubClient = new FakeGitHubRepositoryClient();
        var service = new RepositoryProjectService(repository, unitOfWork, gitHubClient);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.RegisterAsync(new RegisterRepositoryProjectRequest
            {
                Name = "CodeMentor AI",
                RepositoryUrl = "https://example.com/owner/repository",
                Description = null
            }));

        Assert.Equal(0, gitHubClient.CallCount);
        Assert.Null(repository.AddedProject);
        Assert.Equal(0, unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task RegisterAsync_RejectsNonPublicMetadataWithoutSaving()
    {
        var repository = new FakeRepositoryProjectRepository();
        var unitOfWork = new FakeUnitOfWork();
        var gitHubClient = new FakeGitHubRepositoryClient
        {
            Metadata = new GitHubRepositoryMetadata(
                "example",
                "private-repository",
                "https://github.com/example/private-repository",
                "main",
                "private")
        };
        var service = new RepositoryProjectService(repository, unitOfWork, gitHubClient);

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(() =>
            service.RegisterAsync(new RegisterRepositoryProjectRequest
            {
                Name = "Private project",
                RepositoryUrl = "https://github.com/example/private-repository",
                Description = null
            }));

        Assert.Equal(GitHubRepositoryClientError.NotPublic, exception.Error);
        Assert.Null(repository.AddedProject);
        Assert.Equal(0, unitOfWork.SaveChangesCount);
    }

    [Theory]
    [InlineData(GitHubRepositoryClientError.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(GitHubRepositoryClientError.NotPublic, StatusCodes.Status422UnprocessableEntity)]
    [InlineData(GitHubRepositoryClientError.ServerError, StatusCodes.Status502BadGateway)]
    [InlineData(GitHubRepositoryClientError.NetworkError, StatusCodes.Status502BadGateway)]
    [InlineData(GitHubRepositoryClientError.Timeout, StatusCodes.Status502BadGateway)]
    [InlineData(GitHubRepositoryClientError.InvalidResponse, StatusCodes.Status502BadGateway)]
    public async Task Register_ReturnsProblemDetailsForGitHubVerificationFailures(
        GitHubRepositoryClientError error,
        int expectedStatusCode)
    {
        var repository = new FakeRepositoryProjectRepository();
        var unitOfWork = new FakeUnitOfWork();
        var gitHubClient = new FakeGitHubRepositoryClient
        {
            ExceptionToThrow = new GitHubRepositoryClientException(error, "sensitive upstream detail")
        };
        var controller = CreateController(
            new RepositoryProjectService(repository, unitOfWork, gitHubClient));

        var result = await controller.Register(CreateGitHubRequest(), CancellationToken.None);

        var problemResult = Assert.IsType<ObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(problemResult.Value);
        Assert.Equal(expectedStatusCode, problemResult.StatusCode);
        Assert.Equal(expectedStatusCode, problem.Status);
        Assert.DoesNotContain("sensitive upstream detail", problem.Detail);
        Assert.Null(repository.AddedProject);
        Assert.Equal(0, unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Register_ReturnsRateLimitRetryInformationWithoutSaving()
    {
        var retryAt = DateTimeOffset.UtcNow.AddMinutes(2);
        var resetAt = DateTimeOffset.UtcNow.AddMinutes(3);
        var repository = new FakeRepositoryProjectRepository();
        var unitOfWork = new FakeUnitOfWork();
        var gitHubClient = new FakeGitHubRepositoryClient
        {
            ExceptionToThrow = new GitHubRepositoryClientException(
                GitHubRepositoryClientError.RateLimited,
                "sensitive upstream detail",
                retryAfterDelay: TimeSpan.FromSeconds(90),
                retryAfterAtUtc: retryAt,
                rateLimitResetAtUtc: resetAt)
        };
        var controller = CreateController(
            new RepositoryProjectService(repository, unitOfWork, gitHubClient));

        var result = await controller.Register(CreateGitHubRequest(), CancellationToken.None);

        var problemResult = Assert.IsType<ObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(problemResult.Value);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problemResult.StatusCode);
        Assert.Equal(90d, problem.Extensions["retryAfterSeconds"]);
        Assert.Equal(retryAt, problem.Extensions["retryAfterAtUtc"]);
        Assert.Equal(resetAt, problem.Extensions["rateLimitResetAtUtc"]);
        Assert.Equal("90", controller.Response.Headers["Retry-After"]);
        Assert.Null(repository.AddedProject);
        Assert.Equal(0, unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Register_ReturnsBadRequestProblemDetailsForInvalidGitHubUrl()
    {
        var repository = new FakeRepositoryProjectRepository();
        var unitOfWork = new FakeUnitOfWork();
        var gitHubClient = new FakeGitHubRepositoryClient();
        var controller = CreateController(
            new RepositoryProjectService(repository, unitOfWork, gitHubClient));

        var result = await controller.Register(
            new RegisterRepositoryProjectRequest
            {
                Name = "CodeMentor AI",
                RepositoryUrl = "https://example.com/owner/repository",
                Description = null
            },
            CancellationToken.None);

        var problemResult = Assert.IsType<ObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(problemResult.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, problemResult.StatusCode);
        Assert.Equal("Invalid repository URL", problem.Title);
        Assert.Equal(0, gitHubClient.CallCount);
        Assert.Null(repository.AddedProject);
        Assert.Equal(0, unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Register_ReturnsCreatedAtActionForNewProject()
    {
        var repository = new FakeRepositoryProjectRepository();
        var service = new RepositoryProjectService(
            repository,
            new FakeUnitOfWork(),
            new FakeGitHubRepositoryClient());
        var controller = CreateController(service);

        var result = await controller.Register(
            new RegisterRepositoryProjectRequest
            {
                Name = "CodeMentor AI",
                RepositoryUrl = null,
                Description = null
            },
            CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        var response = Assert.IsType<RepositoryProjectResponse>(createdResult.Value);
        Assert.Equal(nameof(RepositoryProjectsController.GetById), createdResult.ActionName);
        Assert.Equal(StatusCodes.Status201Created, createdResult.StatusCode);
        Assert.Equal(response.Id, repository.AddedProject?.Id);
    }

    [Fact]
    public async Task RegisterEndpoint_Returns201WithLocationThatResolvesToProject()
    {
        var repository = new FakeRepositoryProjectRepository();
        var service = new RepositoryProjectService(
            repository,
            new FakeUnitOfWork(),
            new FakeGitHubRepositoryClient());
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services
            .AddControllers()
            .AddApplicationPart(typeof(RepositoryProjectsController).Assembly);
        builder.Services.AddSingleton<IRepositoryProjectService>(service);

        await using var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();

        try
        {
            var server = app.Services.GetRequiredService<IServer>();
            var address = server.Features
                .Get<IServerAddressesFeature>()?
                .Addresses
                .Single()
                ?? throw new InvalidOperationException("The test server did not expose its listening address.");
            using var client = new HttpClient { BaseAddress = new Uri(address) };

            using var createResponse = await client.PostAsJsonAsync(
                "/api/repository-projects",
                new
                {
                    name = "CodeMentor AI",
                    repositoryUrl = (string?)null,
                    description = (string?)null
                });

            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            Assert.NotNull(createResponse.Headers.Location);

            using var getResponse = await client.GetAsync(createResponse.Headers.Location);
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            var response = await getResponse.Content.ReadFromJsonAsync<RepositoryProjectResponse>();
            Assert.NotNull(response);
            Assert.Equal(repository.AddedProject?.Id, response.Id);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task GetById_ReturnsNotFoundWhenProjectDoesNotExist()
    {
        var service = new RepositoryProjectService(
            new FakeRepositoryProjectRepository(),
            new FakeUnitOfWork(),
            new FakeGitHubRepositoryClient());
        var controller = CreateController(service);

        var result = await controller.GetById(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    private static RepositoryProjectsController CreateController(IRepositoryProjectService service) =>
        new(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

    private static RegisterRepositoryProjectRequest CreateGitHubRequest() =>
        new()
        {
            Name = "Caller-provided name",
            RepositoryUrl = "https://github.com/example/repository",
            Description = "Caller-provided description"
        };

    private sealed class FakeGitHubRepositoryClient : IGitHubRepositoryClient
    {
        public GitHubRepositoryMetadata Metadata { get; init; } = new(
            "example",
            "codementor-ai",
            "https://github.com/example/codementor-ai",
            "main",
            "public");

        public GitHubRepositoryClientException? ExceptionToThrow { get; init; }

        public GitHubRepositoryReference? RequestedReference { get; private set; }

        public int CallCount { get; private set; }

        public Task<GitHubRepositoryMetadata> GetPublicRepositoryAsync(
            GitHubRepositoryReference repository,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            RequestedReference = repository;
            return ExceptionToThrow is null
                ? Task.FromResult(Metadata)
                : Task.FromException<GitHubRepositoryMetadata>(ExceptionToThrow);
        }

        public Task<GitHubRepositoryTree> GetRepositoryTreeAsync(
            GitHubRepositoryReference repository,
            CancellationToken cancellationToken = default) =>
            Task.FromException<GitHubRepositoryTree>(
                new NotSupportedException("Repository tree retrieval is not used by these tests."));

        public Task<IReadOnlyList<GitHubRepositoryFileContent>> GetRepositoryFilesAsync(
            GitHubRepositoryReference repository,
            GitHubRepositoryTree tree,
            IReadOnlyCollection<string> selectedPaths,
            CancellationToken cancellationToken = default) =>
            Task.FromException<IReadOnlyList<GitHubRepositoryFileContent>>(
                new NotSupportedException("Repository file retrieval is not used by these tests."));
    }

    private sealed class FakeRepositoryProjectRepository : IRepositoryProjectRepository
    {
        public RepositoryProject? AddedProject { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public Task<RepositoryProject?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromResult(AddedProject?.Id == id ? AddedProject : null);
        }

        public Task<IReadOnlyList<RepositoryProject>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RepositoryProject>>([]);

        public Task AddAsync(RepositoryProject project, CancellationToken cancellationToken = default)
        {
            AddedProject = project;
            LastCancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCount { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCount++;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(1);
        }
    }
}
