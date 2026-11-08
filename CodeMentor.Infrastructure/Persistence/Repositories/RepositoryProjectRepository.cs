using CodeMentor.Application.Abstractions.Persistence;
using CodeMentor.Core.Entities;
using CodeMentor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CodeMentor.Infrastructure.Persistence.Repositories;

internal sealed class RepositoryProjectRepository(CodeMentorDbContext dbContext) : IRepositoryProjectRepository
{
    public Task<RepositoryProject?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.RepositoryProjects
            .AsNoTracking()
            .SingleOrDefaultAsync(project => project.Id == id, cancellationToken);

    public async Task<IReadOnlyList<RepositoryProject>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await dbContext.RepositoryProjects
            .AsNoTracking()
            .OrderBy(project => project.Name)
            .ThenBy(project => project.Id)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(RepositoryProject project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        await dbContext.RepositoryProjects.AddAsync(project, cancellationToken);
    }
}
