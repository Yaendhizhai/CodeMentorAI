using CodeMentor.Application.CodeRetrieval;
using CodeMentor.Core.Entities;

namespace CodeMentor.Application.Abstractions.Persistence;

public interface ICodeChunkRepository
{
    Task<IReadOnlyList<CodeChunk>> GetByRepositoryFileIdAsync(
        Guid repositoryFileId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CodeChunkSearchResult>> SearchAsync(
        Guid projectId,
        IReadOnlyList<string> normalizedTerms,
        int limit,
        CancellationToken cancellationToken = default);

    Task AddAsync(CodeChunk chunk, CancellationToken cancellationToken = default);
}
