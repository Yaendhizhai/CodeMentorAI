namespace CodeMentor.Core.Entities;

public sealed class Conversation
{
    private Conversation()
    {
        Title = string.Empty;
    }

    public Conversation(Guid projectId, string title)
    {
        EntityValidation.RequiredId(projectId, nameof(projectId));

        Id = Guid.NewGuid();
        ProjectId = projectId;
        Title = EntityValidation.RequiredText(title, nameof(title), 200);
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public string Title { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public RepositoryProject Project { get; private set; } = null!;

    public ICollection<ConversationMessage> Messages { get; private set; } = new List<ConversationMessage>();
}
