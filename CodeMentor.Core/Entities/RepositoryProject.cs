namespace CodeMentor.Core.Entities;

public sealed class RepositoryProject
{
    private RepositoryProject()
    {
        Name = string.Empty;
    }

    public RepositoryProject(string name, string? repositoryUrl = null, string? description = null)
    {
        Id = Guid.NewGuid();
        Name = EntityValidation.RequiredText(name, nameof(name), 200);
        RepositoryUrl = EntityValidation.OptionalText(repositoryUrl, nameof(repositoryUrl), 2048);
        Description = EntityValidation.OptionalText(description, nameof(description), 2000);
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public string? RepositoryUrl { get; private set; }

    public string? Description { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public ICollection<RepositoryFile> Files { get; private set; } = new List<RepositoryFile>();

    public ICollection<AnalysisSession> AnalysisSessions { get; private set; } = new List<AnalysisSession>();

    public ICollection<Conversation> Conversations { get; private set; } = new List<Conversation>();
}
