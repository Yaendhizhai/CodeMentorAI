using CodeMentor.Core.Entities;

namespace CodeMentor.Application.Abstractions.Persistence;

public interface IConversationMessageRepository
{
    Task<IReadOnlyList<ConversationMessage>> GetByConversationIdAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);

    Task AddAsync(ConversationMessage message, CancellationToken cancellationToken = default);
}
