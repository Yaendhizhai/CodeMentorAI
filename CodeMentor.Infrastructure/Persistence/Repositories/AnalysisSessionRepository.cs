using CodeMentor.Application.Abstractions.Persistence;
using CodeMentor.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodeMentor.Infrastructure.Persistence.Repositories;

internal sealed class AnalysisSessionRepository(CodeMentorDbContext dbContext) : IAnalysisSessionRepository
{
    public Task<AnalysisSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.AnalysisSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(session => session.Id == id, cancellationToken);

    public async Task<IReadOnlyList<AnalysisSession>> GetByProjectIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        await dbContext.AnalysisSessions
            .AsNoTracking()
            .Where(session => session.ProjectId == projectId)
            .OrderByDescending(session => session.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(AnalysisSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        await dbContext.AnalysisSessions.AddAsync(session, cancellationToken);
    }
}
