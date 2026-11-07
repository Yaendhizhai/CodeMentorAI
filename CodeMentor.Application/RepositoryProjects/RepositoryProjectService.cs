using System.ComponentModel.DataAnnotations;
using CodeMentor.Application.Abstractions.Persistence;
using CodeMentor.Application.GitHub;
using CodeMentor.Core.Entities;

namespace CodeMentor.Application.RepositoryProjects;

public sealed class RepositoryProjectService(
    IRepositoryProjectRepository repository,
    IUnitOfWork unitOfWork,
    IGitHubRepositoryClient gitHubRepositoryClient) : IRepositoryProjectService
{
    public async Task<RepositoryProjectResponse> RegisterAsync(
        RegisterRepositoryProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validator.ValidateObject(request, new ValidationContext(request), validateAllProperties: true);

        var repositoryUrl = request.RepositoryUrl;
        if (repositoryUrl is not null)
        {
            if (!GitHubRepositoryReference.TryParse(repositoryUrl, out var reference) ||
                reference is null)
            {
                throw new ValidationException(
                    "Repository URL must be a valid HTTPS URL for a GitHub repository.");
            }

            var metadata = await gitHubRepositoryClient.GetPublicRepositoryAsync(
                reference,
                cancellationToken);

            if (!string.Equals(metadata.Visibility, "public", StringComparison.OrdinalIgnoreCase))
            {
                throw new GitHubRepositoryClientException(
                    GitHubRepositoryClientError.NotPublic,
                    "The GitHub repository is not public.");
            }

            repositoryUrl = metadata.CanonicalHtmlUrl;
        }

        var project = new RepositoryProject(request.Name, repositoryUrl, request.Description);
        await repository.AddAsync(project, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToResponse(project);
    }

    public async Task<RepositoryProjectResponse?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var project = await repository.GetByIdAsync(id, cancellationToken);
        return project is null ? null : ToResponse(project);
    }

    private static RepositoryProjectResponse ToResponse(RepositoryProject project) =>
        new(
            project.Id,
            project.Name,
            project.RepositoryUrl,
            project.Description,
            project.CreatedAtUtc,
            project.UpdatedAtUtc);
}
