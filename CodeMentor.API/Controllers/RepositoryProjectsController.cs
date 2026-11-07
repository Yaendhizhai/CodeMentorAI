using System.ComponentModel.DataAnnotations;
using CodeMentor.Application.GitHub;
using CodeMentor.Application.RepositoryProjects;
using Microsoft.AspNetCore.Mvc;

namespace CodeMentor.API.Controllers;

[ApiController]
[Route("api/repository-projects")]
public sealed class RepositoryProjectsController(IRepositoryProjectService repositoryProjectService)
    : ControllerBase
{
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
