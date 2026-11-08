using System.ComponentModel.DataAnnotations;
using CodeMentor.Application.GitHub;
using CodeMentor.Application.RepositoryIngestion;
using CodeMentor.Application.RepositoryProjects;
using Microsoft.AspNetCore.Mvc;

namespace CodeMentor.API.Controllers;

[ApiController]
[Route("api/repository-projects")]
public sealed class RepositoryProjectsController(
    IRepositoryProjectService repositoryProjectService,
    IRepositoryIngestionService repositoryIngestionService)
    : ControllerBase
{
    private const string AlreadyIngestedErrorMessage =
        "The repository project has already been ingested.";

    /// <summary>Registers a repository project.</summary>
    /// <param name="request">The repository project details.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The created repository project.</returns>
    [HttpPost]
    [ProducesResponseType<RepositoryProjectResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<RepositoryProjectResponse>> Register(
        [FromBody] RegisterRepositoryProjectRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var project = await repositoryProjectService.RegisterAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = project.Id }, project);
        }
        catch (ValidationException)
        {
            return Problem(
                title: "Invalid repository URL",
                detail: "Provide a valid HTTPS URL for a GitHub repository.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        catch (GitHubRepositoryClientException exception)
        {
            return CreateGitHubErrorProblem(exception);
        }
    }

    /// <summary>Gets a repository project by ID.</summary>
    /// <param name="id">The repository project ID.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The requested repository project, if it exists.</returns>
    [HttpGet("{id:guid}", Name = nameof(GetById))]
    [ProducesResponseType<RepositoryProjectResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RepositoryProjectResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var project = await repositoryProjectService.GetByIdAsync(id, cancellationToken);
        return project is null ? NotFound() : Ok(project);
    }

    /// <summary>Gets all repository projects.</summary>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The repository projects, or an empty collection.</returns>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RepositoryProjectResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RepositoryProjectResponse>>> GetAll(
        CancellationToken cancellationToken)
    {
        var projects = await repositoryProjectService.GetAllAsync(cancellationToken);
        return Ok(projects);
    }

    /// <summary>Ingests a repository project's source files and code chunks.</summary>
    /// <param name="id">The repository project ID.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The completed repository ingestion result.</returns>
    [HttpPost("{id:guid}/ingest")]
    [ProducesResponseType<RepositoryIngestionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<RepositoryIngestionResponse>> Ingest(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await repositoryIngestionService.IngestAsync(id, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return Problem(
                title: "Repository project not found",
                detail: "The requested repository project does not exist.",
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (InvalidOperationException exception) when (
            string.Equals(exception.Message, AlreadyIngestedErrorMessage, StringComparison.Ordinal))
        {
            return Problem(
                title: "Repository already ingested",
                detail: "This repository project has already been ingested.",
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (ValidationException)
        {
            return Problem(
                title: "Invalid repository configuration",
                detail: "The repository project does not have a valid public GitHub URL.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
        catch (ArgumentException)
        {
            return Problem(
                title: "Invalid repository project ID",
                detail: "A valid repository project ID is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        catch (GitHubRepositoryClientException exception)
        {
            return CreateIngestionGitHubErrorProblem(exception);
        }
    }

    private ObjectResult CreateIngestionGitHubErrorProblem(GitHubRepositoryClientException exception)
    {
        if (exception.Error == GitHubRepositoryClientError.RateLimited)
        {
            return CreateGitHubErrorProblem(exception);
        }

        var (statusCode, title, detail) = exception.Error switch
        {
            GitHubRepositoryClientError.TreeTooLarge or
            GitHubRepositoryClientError.TreeTruncated or
            GitHubRepositoryClientError.TooManyFiles or
            GitHubRepositoryClientError.FileTooLarge or
            GitHubRepositoryClientError.TotalDownloadTooLarge or
            GitHubRepositoryClientError.ResponseTooLarge =>
                (StatusCodes.Status413PayloadTooLarge, "Repository exceeds ingestion limits",
                    "The repository contains more source data than can be ingested in one operation."),
            _ =>
                (StatusCodes.Status502BadGateway, "GitHub ingestion failed",
                    "The repository could not be retrieved from GitHub.")
        };

        return Problem(
            title: title,
            detail: detail,
            statusCode: statusCode);
    }

    private ObjectResult CreateGitHubErrorProblem(GitHubRepositoryClientException exception)
    {
        var (statusCode, title, detail) = exception.Error switch
        {
            GitHubRepositoryClientError.NotFound =>
                (StatusCodes.Status404NotFound, "GitHub repository not found",
                    "The specified GitHub repository could not be found."),
            GitHubRepositoryClientError.NotPublic =>
                (StatusCodes.Status422UnprocessableEntity, "GitHub repository is not public",
                    "Only public GitHub repositories can be registered."),
            GitHubRepositoryClientError.RateLimited =>
                (StatusCodes.Status503ServiceUnavailable, "GitHub rate limit reached",
                    "GitHub is temporarily limiting repository verification requests."),
            _ =>
                (StatusCodes.Status502BadGateway, "GitHub verification failed",
                    "The GitHub repository could not be verified.")
        };

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = HttpContext.Request.Path
        };

        if (exception.Error == GitHubRepositoryClientError.RateLimited)
        {
            if (exception.RetryAfterDelay is { } retryAfterDelay)
            {
                problem.Extensions["retryAfterSeconds"] = Math.Max(0, retryAfterDelay.TotalSeconds);
                Response.Headers["Retry-After"] = Math.Max(0, (int)Math.Ceiling(retryAfterDelay.TotalSeconds))
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (exception.RetryAfterAtUtc is { } retryAfterAtUtc)
            {
                problem.Extensions["retryAfterAtUtc"] = retryAfterAtUtc;
                if (exception.RetryAfterDelay is null)
                {
                    Response.Headers["Retry-After"] = retryAfterAtUtc.ToString("R");
                }
            }

            if (exception.RateLimitResetAtUtc is { } rateLimitResetAtUtc)
            {
                problem.Extensions["rateLimitResetAtUtc"] = rateLimitResetAtUtc;
                if (exception.RetryAfterDelay is null && exception.RetryAfterAtUtc is null)
                {
                    Response.Headers["Retry-After"] = rateLimitResetAtUtc.ToString("R");
                }
            }
        }

        return new ObjectResult(problem) { StatusCode = statusCode };
    }
}
