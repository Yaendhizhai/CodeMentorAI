using CodeMentor.Core.Entities;

namespace CodeMentor.Application.Abstractions.Persistence;

public interface ICodeChunkRepository
{
    Task<IReadOnlyList<CodeChunk>> GetByRepositoryFileIdAsync(
        Guid repositoryFileId,
        CancellationToken cancellationToken = default);

    Task AddAsync(CodeChunk chunk, CancellationToken cancellationToken = default);
}
