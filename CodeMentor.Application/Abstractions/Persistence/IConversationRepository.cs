using CodeMentor.Core.Entities;

namespace CodeMentor.Application.Abstractions.Persistence;

public interface IConversationRepository
{
    Task<Conversation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Conversation>> GetByProjectIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task AddAsync(Conversation conversation, CancellationToken cancellationToken = default);
}
