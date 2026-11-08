using CodeMentor.Application.Abstractions.Persistence;
using CodeMentor.Application.CodeRetrieval;
using CodeMentor.Core.Entities;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

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

    public async Task<IReadOnlyList<CodeChunkSearchResult>> SearchAsync(
        Guid projectId,
        IReadOnlyList<string> normalizedTerms,
        int limit,
        CancellationToken cancellationToken = default) =>
        await BuildSearchQuery(projectId, normalizedTerms, limit)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(CodeChunk chunk, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        await dbContext.CodeChunks.AddAsync(chunk, cancellationToken);
    }

    internal IQueryable<CodeChunkSearchResult> BuildSearchQuery(
        Guid projectId,
        IReadOnlyList<string> normalizedTerms,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(normalizedTerms);
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("A repository project ID is required.", nameof(projectId));
        }

        if (normalizedTerms.Count == 0 || normalizedTerms.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one normalized search term is required.", nameof(normalizedTerms));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var chunkParameter = Expression.Parameter(typeof(CodeChunk), "chunk");
        var contentExpression = Expression.Property(chunkParameter, nameof(CodeChunk.Content));
        var containsMethod = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
        Expression? matchExpression = null;
        Expression? scoreExpression = null;

        foreach (var term in normalizedTerms)
        {
            var termMatch = Expression.Call(
                contentExpression,
                containsMethod,
                Expression.Constant(term));
            matchExpression = matchExpression is null
                ? termMatch
                : Expression.OrElse(matchExpression, termMatch);
            var scoreTerm = Expression.Condition(
                termMatch,
                Expression.Constant(1),
                Expression.Constant(0));
            scoreExpression = scoreExpression is null
                ? scoreTerm
                : Expression.Add(scoreExpression, scoreTerm);
        }

        var scoreSelector = Expression.Lambda<Func<CodeChunk, int>>(scoreExpression!, chunkParameter);
        var query = dbContext.CodeChunks
            .AsNoTracking()
            .Where(chunk => chunk.RepositoryFile.ProjectId == projectId)
            .Where(Expression.Lambda<Func<CodeChunk, bool>>(matchExpression!, chunkParameter))
            .OrderByDescending(scoreSelector)
            .ThenBy(chunk => chunk.RepositoryFile.RelativePath)
            .ThenBy(chunk => chunk.ChunkIndex)
            .ThenBy(chunk => chunk.Id)
            .Take(limit);

        var resultConstructor = typeof(CodeChunkSearchResult).GetConstructors().Single();
        var resultSelector = Expression.Lambda<Func<CodeChunk, CodeChunkSearchResult>>(
            Expression.New(
                resultConstructor,
                Expression.Property(
                    Expression.Property(chunkParameter, nameof(CodeChunk.RepositoryFile)),
                    nameof(RepositoryFile.RelativePath)),
                Expression.Property(chunkParameter, nameof(CodeChunk.ChunkIndex)),
                Expression.Property(chunkParameter, nameof(CodeChunk.StartLine)),
                Expression.Property(chunkParameter, nameof(CodeChunk.EndLine)),
                contentExpression,
                scoreExpression!),
            chunkParameter);

        return query.Select(resultSelector);
    }
}
