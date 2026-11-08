using System.ComponentModel.DataAnnotations;
using CodeMentor.Application.Abstractions.Persistence;
using CodeMentor.Application.CodeRetrieval;
using CodeMentor.Application.GitHub;
using CodeMentor.Application.RepositoryIngestion;
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
    public async Task Ingest_ReturnsOkWithIngestionResponse()
    {
        var expected = CreateIngestionResponse();
        var ingestionService = new FakeRepositoryIngestionService { Response = expected };
        var controller = CreateController(
            new FakeRepositoryProjectService(),
            ingestionService);

        var result = await controller.Ingest(expected.RepositoryProjectId, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(expected, okResult.Value);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);
    }

    [Fact]
    public async Task Ingest_ReturnsNotFoundWhenProjectDoesNotExist()
    {
        var controller = CreateController(new FakeRepositoryIngestionService
        {
            ExceptionToThrow = new KeyNotFoundException("sensitive project detail")
        });

        var result = await controller.Ingest(Guid.NewGuid(), CancellationToken.None);

        AssertIngestionProblem(result.Result, StatusCodes.Status404NotFound, "sensitive project detail");
    }

    [Fact]
    public async Task Ingest_ReturnsConflictWhenProjectWasAlreadyIngested()
    {
        var controller = CreateController(new FakeRepositoryIngestionService
        {
            ExceptionToThrow = new InvalidOperationException("The repository project has already been ingested.")
        });

        var result = await controller.Ingest(Guid.NewGuid(), CancellationToken.None);

        var problemResult = Assert.IsType<ObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(problemResult.Value);
        Assert.Equal(StatusCodes.Status409Conflict, problemResult.StatusCode);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
        Assert.Equal("This repository project has already been ingested.", problem.Detail);
    }

    [Fact]
    public async Task Ingest_PropagatesUnexpectedInvalidOperationException()
    {
        var exception = new InvalidOperationException("unexpected persistence failure");
        var controller = CreateController(new FakeRepositoryIngestionService
        {
            ExceptionToThrow = exception
        });

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.Ingest(Guid.NewGuid(), CancellationToken.None));

        Assert.Same(exception, actual);
    }

    [Fact]
    public async Task Ingest_ReturnsUnprocessableEntityForInvalidStoredRepositoryUrl()
    {
        var controller = CreateController(new FakeRepositoryIngestionService
        {
            ExceptionToThrow = new ValidationException("sensitive repository detail")
        });

        var result = await controller.Ingest(Guid.NewGuid(), CancellationToken.None);

        AssertIngestionProblem(result.Result, StatusCodes.Status422UnprocessableEntity, "sensitive repository detail");
    }

    [Fact]
    public async Task Ingest_ReturnsBadRequestForInvalidProjectId()
    {
        var controller = CreateController(new FakeRepositoryIngestionService
        {
            ExceptionToThrow = new ArgumentException("sensitive argument detail")
        });

        var result = await controller.Ingest(Guid.Empty, CancellationToken.None);

        AssertIngestionProblem(result.Result, StatusCodes.Status400BadRequest, "sensitive argument detail");
    }

    [Theory]
    [InlineData(GitHubRepositoryClientError.ServerError)]
    [InlineData(GitHubRepositoryClientError.NotFound)]
    [InlineData(GitHubRepositoryClientError.NotPublic)]
    [InlineData(GitHubRepositoryClientError.InvalidResponse)]
    public async Task Ingest_ReturnsBadGatewayForOtherGitHubErrors(GitHubRepositoryClientError error)
    {
        var controller = CreateController(new FakeRepositoryIngestionService
        {
            ExceptionToThrow = new GitHubRepositoryClientException(error, "sensitive upstream detail")
        });

        var result = await controller.Ingest(Guid.NewGuid(), CancellationToken.None);

        AssertIngestionProblem(result.Result, StatusCodes.Status502BadGateway, "sensitive upstream detail");
    }

    [Theory]
    [InlineData(GitHubRepositoryClientError.TreeTooLarge)]
    [InlineData(GitHubRepositoryClientError.TreeTruncated)]
    [InlineData(GitHubRepositoryClientError.TooManyFiles)]
    [InlineData(GitHubRepositoryClientError.FileTooLarge)]
    [InlineData(GitHubRepositoryClientError.TotalDownloadTooLarge)]
    [InlineData(GitHubRepositoryClientError.ResponseTooLarge)]
    public async Task Ingest_ReturnsPayloadTooLargeForIngestionLimitErrors(GitHubRepositoryClientError error)
    {
        var controller = CreateController(new FakeRepositoryIngestionService
        {
            ExceptionToThrow = new GitHubRepositoryClientException(error, "sensitive upstream detail")
        });

        var result = await controller.Ingest(Guid.NewGuid(), CancellationToken.None);

        AssertIngestionProblem(result.Result, StatusCodes.Status413PayloadTooLarge, "sensitive upstream detail");
    }

    [Fact]
    public async Task Ingest_ReturnsServiceUnavailableAndRetryAfterForGitHubRateLimit()
    {
        var retryAt = DateTimeOffset.UtcNow.AddMinutes(2);
        var resetAt = DateTimeOffset.UtcNow.AddMinutes(3);
        var controller = CreateController(new FakeRepositoryIngestionService
        {
            ExceptionToThrow = new GitHubRepositoryClientException(
                GitHubRepositoryClientError.RateLimited,
                "sensitive upstream detail",
                retryAfterDelay: TimeSpan.FromSeconds(90),
                retryAfterAtUtc: retryAt,
                rateLimitResetAtUtc: resetAt)
        });

        var result = await controller.Ingest(Guid.NewGuid(), CancellationToken.None);

        var problemResult = Assert.IsType<ObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(problemResult.Value);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problemResult.StatusCode);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.Status);
        Assert.DoesNotContain("sensitive upstream detail", problem.Detail);
        Assert.Equal(90d, problem.Extensions["retryAfterSeconds"]);
        Assert.Equal(retryAt, problem.Extensions["retryAfterAtUtc"]);
        Assert.Equal(resetAt, problem.Extensions["rateLimitResetAtUtc"]);
        Assert.Equal("90", controller.Response.Headers["Retry-After"]);
    }

    [Fact]
    public async Task Ingest_ForwardsCancellationTokenUnchanged()
    {
        var ingestionService = new FakeRepositoryIngestionService();
        var controller = CreateController(ingestionService);
        using var cancellationSource = new CancellationTokenSource();

        await controller.Ingest(Guid.NewGuid(), cancellationSource.Token);

        Assert.Equal(cancellationSource.Token, ingestionService.LastCancellationToken);
    }

    [Fact]
    public async Task Ingest_PropagatesCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        var cancellation = new OperationCanceledException(cancellationSource.Token);
        var controller = CreateController(new FakeRepositoryIngestionService
        {
            ExceptionToThrow = cancellation
        });

        var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => controller.Ingest(Guid.NewGuid(), cancellationSource.Token));

        Assert.Same(cancellation, actual);
    }

    [Fact]
    public async Task IngestEndpoint_Returns200WithIngestionResponse()
    {
        var expected = CreateIngestionResponse();
        var ingestionService = new FakeRepositoryIngestionService { Response = expected };
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services
            .AddControllers()
            .AddApplicationPart(typeof(RepositoryProjectsController).Assembly);
        builder.Services.AddSingleton<IRepositoryProjectService>(new FakeRepositoryProjectService());
        builder.Services.AddSingleton<IRepositoryIngestionService>(ingestionService);
        builder.Services.AddSingleton<ICodeRetrievalService>(new FakeCodeRetrievalService());

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

            using var response = await client.PostAsync(
                $"/api/repository-projects/{expected.RepositoryProjectId}/ingest",
                content: null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var actual = await response.Content.ReadFromJsonAsync<RepositoryIngestionResponse>();
            Assert.Equal(expected, actual);
            Assert.Equal(expected.RepositoryProjectId, ingestionService.LastRepositoryProjectId);
        }
        finally
        {
            await app.StopAsync();
        }
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
        builder.Services.AddSingleton<IRepositoryIngestionService>(new FakeRepositoryIngestionService());
        builder.Services.AddSingleton<ICodeRetrievalService>(new FakeCodeRetrievalService());

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

    [Fact]
    public async Task GetAllAsync_ReturnsMappedProjectsAndForwardsCancellationToken()
    {
        var projects = new[]
        {
            new RepositoryProject(
                "First project",
                "https://github.com/example/first",
                "First description"),
            new RepositoryProject(
                "Second project",
                "https://github.com/example/second",
                "Second description")
        };
        var repository = new FakeRepositoryProjectRepository { Projects = projects };
        var service = new RepositoryProjectService(
            repository,
            new FakeUnitOfWork(),
            new FakeGitHubRepositoryClient());
        using var cancellationSource = new CancellationTokenSource();

        var responses = await service.GetAllAsync(cancellationSource.Token);

        Assert.Equal(cancellationSource.Token, repository.LastGetAllCancellationToken);
        Assert.Collection(
            responses,
            first =>
            {
                Assert.Equal(projects[0].Id, first.Id);
                Assert.Equal(projects[0].Name, first.Name);
                Assert.Equal(projects[0].RepositoryUrl, first.RepositoryUrl);
                Assert.Equal(projects[0].Description, first.Description);
                Assert.Equal(projects[0].CreatedAtUtc, first.CreatedAtUtc);
                Assert.Equal(projects[0].UpdatedAtUtc, first.UpdatedAtUtc);
            },
            second =>
            {
                Assert.Equal(projects[1].Id, second.Id);
                Assert.Equal(projects[1].Name, second.Name);
                Assert.Equal(projects[1].RepositoryUrl, second.RepositoryUrl);
                Assert.Equal(projects[1].Description, second.Description);
                Assert.Equal(projects[1].CreatedAtUtc, second.CreatedAtUtc);
                Assert.Equal(projects[1].UpdatedAtUtc, second.UpdatedAtUtc);
            });
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEmptyCollectionWhenNoProjectsExist()
    {
        var service = new RepositoryProjectService(
            new FakeRepositoryProjectRepository(),
            new FakeUnitOfWork(),
            new FakeGitHubRepositoryClient());

        var responses = await service.GetAllAsync();

        Assert.Empty(responses);
    }

    [Fact]
    public async Task GetAllEndpoint_ReturnsOkWithProjectCollection()
    {
        var projects = new[]
        {
            new RepositoryProject("Alpha", "https://github.com/example/alpha", "Alpha project"),
            new RepositoryProject("Beta", null, null)
        };
        var repository = new FakeRepositoryProjectRepository { Projects = projects };
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
        builder.Services.AddSingleton<IRepositoryIngestionService>(new FakeRepositoryIngestionService());
        builder.Services.AddSingleton<ICodeRetrievalService>(new FakeCodeRetrievalService());

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

            using var response = await client.GetAsync("/api/repository-projects");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var results = await response.Content.ReadFromJsonAsync<RepositoryProjectResponse[]>();
            Assert.NotNull(results);
            Assert.Collection(
                results,
                first =>
                {
                    Assert.Equal(projects[0].Id, first.Id);
                    Assert.Equal(projects[0].Name, first.Name);
                    Assert.Equal(projects[0].RepositoryUrl, first.RepositoryUrl);
                    Assert.Equal(projects[0].Description, first.Description);
                    Assert.Equal(projects[0].CreatedAtUtc, first.CreatedAtUtc);
                    Assert.Equal(projects[0].UpdatedAtUtc, first.UpdatedAtUtc);
                },
                second =>
                {
                    Assert.Equal(projects[1].Id, second.Id);
                    Assert.Equal(projects[1].Name, second.Name);
                    Assert.Null(second.RepositoryUrl);
                    Assert.Null(second.Description);
                    Assert.Equal(projects[1].CreatedAtUtc, second.CreatedAtUtc);
                    Assert.Equal(projects[1].UpdatedAtUtc, second.UpdatedAtUtc);
                });
            Assert.NotEqual(default, repository.LastGetAllCancellationToken);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static RepositoryProjectsController CreateController(
        IRepositoryProjectService? service = null,
        IRepositoryIngestionService? ingestionService = null) =>
        new(
            service ?? new FakeRepositoryProjectService(),
            ingestionService ?? new FakeRepositoryIngestionService(),
            new FakeCodeRetrievalService())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

    private static RepositoryProjectsController CreateController(
        IRepositoryIngestionService ingestionService) =>
        new(new FakeRepositoryProjectService(), ingestionService, new FakeCodeRetrievalService())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

    private static RepositoryIngestionResponse CreateIngestionResponse() =>
        new(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Guid.Parse("20000000-0000-0000-0000-000000000002"),
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            3,
            5,
            new DateTimeOffset(2026, 11, 8, 8, 0, 0, TimeSpan.Zero));

    private static void AssertIngestionProblem(
        IActionResult? actionResult,
        int expectedStatusCode,
        string sensitiveDetail)
    {
        var problemResult = Assert.IsType<ObjectResult>(actionResult);
        var problem = Assert.IsType<ProblemDetails>(problemResult.Value);
        Assert.Equal(expectedStatusCode, problemResult.StatusCode);
        Assert.Equal(expectedStatusCode, problem.Status);
        Assert.DoesNotContain(sensitiveDetail, problem.Detail);
    }

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

        public IReadOnlyList<RepositoryProject> Projects { get; init; } = [];

        public CancellationToken LastCancellationToken { get; private set; }

        public CancellationToken LastGetAllCancellationToken { get; private set; }

        public Task<RepositoryProject?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromResult(AddedProject?.Id == id ? AddedProject : null);
        }

        public Task<IReadOnlyList<RepositoryProject>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            LastGetAllCancellationToken = cancellationToken;
            return Task.FromResult(Projects);
        }

        public Task AddAsync(RepositoryProject project, CancellationToken cancellationToken = default)
        {
            AddedProject = project;
            LastCancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRepositoryProjectService : IRepositoryProjectService
    {
        public Task<RepositoryProjectResponse> RegisterAsync(
            RegisterRepositoryProjectRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new RepositoryProjectResponse(
                Guid.Parse("30000000-0000-0000-0000-000000000003"),
                request.Name,
                request.RepositoryUrl,
                request.Description,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow));

        public Task<RepositoryProjectResponse?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<RepositoryProjectResponse?>(null);

        public Task<IReadOnlyList<RepositoryProjectResponse>> GetAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RepositoryProjectResponse>>([]);
    }

    private sealed class FakeRepositoryIngestionService : IRepositoryIngestionService
    {
        public RepositoryIngestionResponse Response { get; init; } = CreateIngestionResponse();

        public Exception? ExceptionToThrow { get; init; }

        public Guid? LastRepositoryProjectId { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public Task<RepositoryIngestionResponse> IngestAsync(
            Guid repositoryProjectId,
            CancellationToken cancellationToken = default)
        {
            LastRepositoryProjectId = repositoryProjectId;
            LastCancellationToken = cancellationToken;
            return ExceptionToThrow is null
                ? Task.FromResult(Response)
                : Task.FromException<RepositoryIngestionResponse>(ExceptionToThrow);
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

        public Task ExecuteInTransactionAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken = default) =>
            operation(cancellationToken);
    }
}
