using CodeMentor.Application.Abstractions.Persistence;
using CodeMentor.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodeMentor.Infrastructure.Persistence.Repositories;

internal sealed class RepositoryFileRepository(CodeMentorDbContext dbContext) : IRepositoryFileRepository
{
    public Task<RepositoryFile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.RepositoryFiles
            .AsNoTracking()
            .SingleOrDefaultAsync(file => file.Id == id, cancellationToken);

    public async Task<IReadOnlyList<RepositoryFile>> GetByProjectIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        await dbContext.RepositoryFiles
            .AsNoTracking()
            .Where(file => file.ProjectId == projectId)
            .OrderBy(file => file.RelativePath)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(RepositoryFile file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        await dbContext.RepositoryFiles.AddAsync(file, cancellationToken);
    }
}
