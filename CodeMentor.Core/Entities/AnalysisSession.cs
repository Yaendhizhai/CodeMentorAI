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

    public void MarkRunning(DateTimeOffset startedAtUtc)
    {
        EnsureUtc(startedAtUtc, nameof(startedAtUtc));
        if (Status != AnalysisSessionStatus.Queued)
        {
            throw new InvalidOperationException("Only a queued analysis session can be started.");
        }

        if (startedAtUtc < CreatedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startedAtUtc),
                "The start timestamp cannot precede the creation timestamp.");
        }

        Status = AnalysisSessionStatus.Running;
        StartedAtUtc = startedAtUtc;
    }

    public void MarkCompleted(DateTimeOffset completedAtUtc)
    {
        MarkTerminal(AnalysisSessionStatus.Completed, completedAtUtc);
    }

    public void MarkFailed(DateTimeOffset completedAtUtc)
    {
        MarkTerminal(AnalysisSessionStatus.Failed, completedAtUtc);
    }

    public void MarkCancelled(DateTimeOffset completedAtUtc)
    {
        MarkTerminal(AnalysisSessionStatus.Cancelled, completedAtUtc);
    }

    private void MarkTerminal(AnalysisSessionStatus status, DateTimeOffset completedAtUtc)
    {
        EnsureUtc(completedAtUtc, nameof(completedAtUtc));
        if (Status != AnalysisSessionStatus.Running || StartedAtUtc is null)
        {
            throw new InvalidOperationException("Only a running analysis session can be completed.");
        }

        if (completedAtUtc < StartedAtUtc.Value)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedAtUtc),
                "The completion timestamp cannot precede the start timestamp.");
        }

        Status = status;
        CompletedAtUtc = completedAtUtc;
    }

    private static void EnsureUtc(DateTimeOffset timestamp, string parameterName)
    {
        if (timestamp.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Analysis session timestamps must use UTC.", parameterName);
        }
    }
}
