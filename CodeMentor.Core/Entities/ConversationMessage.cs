using CodeMentor.Core.Enums;

namespace CodeMentor.Core.Entities;

public sealed class ConversationMessage
{
    private ConversationMessage()
    {
        Content = string.Empty;
    }

    public ConversationMessage(
        Guid conversationId,
        int sequence,
        ConversationMessageRole role,
        string content)
    {
        EntityValidation.RequiredId(conversationId, nameof(conversationId));
        if (sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "Message sequence cannot be negative.");
        }

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), "Message role is not recognized.");
        }

        Id = Guid.NewGuid();
        ConversationId = conversationId;
        Sequence = sequence;
        Role = role;
        Content = EntityValidation.RequiredText(content, nameof(content), int.MaxValue);
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid ConversationId { get; private set; }

    public int Sequence { get; private set; }

    public ConversationMessageRole Role { get; private set; }

    public string Content { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Conversation Conversation { get; private set; } = null!;
}
