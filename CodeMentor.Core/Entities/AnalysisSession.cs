using CodeMentor.Core.Enums;

namespace CodeMentor.Core.Entities;

public sealed class AnalysisSession
{
    private AnalysisSession()
    {
    }

    public AnalysisSession(Guid projectId)
    {
        EntityValidation.RequiredId(projectId, nameof(projectId));

        Id = Guid.NewGuid();
        ProjectId = projectId;
        Status = AnalysisSessionStatus.Queued;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public AnalysisSessionStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? StartedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public RepositoryProject Project { get; private set; } = null!;
}
