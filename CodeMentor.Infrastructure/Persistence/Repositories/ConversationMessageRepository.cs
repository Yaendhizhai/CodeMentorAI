using CodeMentor.Application.Abstractions.Persistence;
using CodeMentor.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodeMentor.Infrastructure.Persistence.Repositories;

internal sealed class ConversationMessageRepository(CodeMentorDbContext dbContext)
    : IConversationMessageRepository
{
    public async Task<IReadOnlyList<ConversationMessage>> GetByConversationIdAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default) =>
        await dbContext.ConversationMessages
            .AsNoTracking()
            .Where(message => message.ConversationId == conversationId)
            .OrderBy(message => message.Sequence)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(ConversationMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        await dbContext.ConversationMessages.AddAsync(message, cancellationToken);
    }
}
