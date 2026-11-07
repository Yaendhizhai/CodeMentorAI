using CodeMentor.Core.Entities;

namespace CodeMentor.Application.Abstractions.Persistence;

public interface IRepositoryProjectRepository
{
    Task<RepositoryProject?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RepositoryProject>> GetAllAsync(CancellationToken cancellationToken = default);

    Task AddAsync(RepositoryProject project, CancellationToken cancellationToken = default);
}
