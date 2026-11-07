using CodeMentor.Application.Abstractions.Persistence;
using CodeMentor.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodeMentor.Infrastructure.Persistence.Repositories;

internal sealed class ConversationRepository(CodeMentorDbContext dbContext) : IConversationRepository
{
    public Task<Conversation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Conversations
            .AsNoTracking()
            .SingleOrDefaultAsync(conversation => conversation.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Conversation>> GetByProjectIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        await dbContext.Conversations
            .AsNoTracking()
            .Where(conversation => conversation.ProjectId == projectId)
            .OrderByDescending(conversation => conversation.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        await dbContext.Conversations.AddAsync(conversation, cancellationToken);
    }
}
