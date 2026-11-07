namespace CodeMentor.Application.RepositoryProjects;

public interface IRepositoryProjectService
{
    Task<RepositoryProjectResponse> RegisterAsync(
        RegisterRepositoryProjectRequest request,
        CancellationToken cancellationToken = default);

    Task<RepositoryProjectResponse?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
