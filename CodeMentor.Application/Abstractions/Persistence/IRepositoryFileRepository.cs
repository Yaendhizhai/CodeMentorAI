using CodeMentor.Core.Entities;

namespace CodeMentor.Application.Abstractions.Persistence;

public interface IRepositoryFileRepository
{
    Task<RepositoryFile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RepositoryFile>> GetByProjectIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task AddAsync(RepositoryFile file, CancellationToken cancellationToken = default);
}
