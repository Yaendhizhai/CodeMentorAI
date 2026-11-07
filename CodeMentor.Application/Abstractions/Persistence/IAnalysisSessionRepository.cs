using CodeMentor.Core.Entities;

namespace CodeMentor.Application.Abstractions.Persistence;

public interface IAnalysisSessionRepository
{
    Task<AnalysisSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AnalysisSession>> GetByProjectIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task AddAsync(AnalysisSession session, CancellationToken cancellationToken = default);

    void Update(AnalysisSession session);
}
