using CodeMentor.Application.Abstractions.Persistence;
using CodeMentor.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodeMentor.Infrastructure.Persistence.Repositories;

internal sealed class CodeChunkRepository(CodeMentorDbContext dbContext) : ICodeChunkRepository
{
    public async Task<IReadOnlyList<CodeChunk>> GetByRepositoryFileIdAsync(
        Guid repositoryFileId,
        CancellationToken cancellationToken = default) =>
        await dbContext.CodeChunks
            .AsNoTracking()
            .Where(chunk => chunk.RepositoryFileId == repositoryFileId)
            .OrderBy(chunk => chunk.ChunkIndex)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(CodeChunk chunk, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        await dbContext.CodeChunks.AddAsync(chunk, cancellationToken);
    }
}
