using CodeMentor.Infrastructure.Persistence;
using CodeMentor.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace CodeMentor.Tests;

public sealed class CodeChunkRepositoryQueryTests
{
    [Fact]
    public void BuildSearchQuery_ScopesMatchesToProjectAndTranslatesRankingAndLimit()
    {
        var options = new DbContextOptionsBuilder<CodeMentorDbContext>()
            .UseSqlServer(
                "Server=localhost;Database=CodeMentorAI_QueryTranslation;Integrated Security=true;TrustServerCertificate=true")
            .Options;
        using var dbContext = new CodeMentorDbContext(options);
        var repository = new CodeChunkRepository(dbContext);

        var sql = repository.BuildSearchQuery(
                Guid.NewGuid(),
                ["widget", "class"],
                7)
            .ToQueryString();

        Assert.Contains("TOP(@p)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DECLARE @p int = 7", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[ProjectId] =", sql, StringComparison.Ordinal);
        Assert.Contains("LIKE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[RelativePath]", sql, StringComparison.Ordinal);
        Assert.Contains("[ChunkIndex]", sql, StringComparison.Ordinal);
        Assert.Contains("[Id]", sql, StringComparison.Ordinal);
        Assert.Contains("CASE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("widget", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GetByRepositoryFileId", sql, StringComparison.Ordinal);

        var orderByIndex = sql.IndexOf("ORDER BY", StringComparison.OrdinalIgnoreCase);
        Assert.True(orderByIndex >= 0);
        var orderByClause = sql[orderByIndex..];
        var scoreOrderIndex = orderByClause.IndexOf("DESC", StringComparison.OrdinalIgnoreCase);
        var pathOrderIndex = orderByClause.IndexOf("[RelativePath]", StringComparison.Ordinal);
        var chunkIndexOrderIndex = orderByClause.IndexOf("[ChunkIndex]", StringComparison.Ordinal);
        var chunkIdOrderIndex = orderByClause.IndexOf("[Id]", StringComparison.Ordinal);
        Assert.True(
            scoreOrderIndex >= 0 &&
            scoreOrderIndex < pathOrderIndex &&
            pathOrderIndex < chunkIndexOrderIndex &&
            chunkIndexOrderIndex < chunkIdOrderIndex,
            $"Unexpected deterministic ranking order: {orderByClause}");
    }

    [Fact]
    public void BuildSearchQuery_UsesNoTrackingAndRejectsInvalidQueryArguments()
    {
        var options = new DbContextOptionsBuilder<CodeMentorDbContext>()
            .UseSqlServer(
                "Server=localhost;Database=CodeMentorAI_QueryTranslation;Integrated Security=true;TrustServerCertificate=true")
            .Options;
        using var dbContext = new CodeMentorDbContext(options);
        var repository = new CodeChunkRepository(dbContext);

        _ = repository.BuildSearchQuery(Guid.NewGuid(), ["term"], 1).ToQueryString();
        Assert.Empty(dbContext.ChangeTracker.Entries());

        Assert.Throws<ArgumentException>(
            () => repository.BuildSearchQuery(Guid.Empty, ["term"], 1));
        Assert.Throws<ArgumentException>(
            () => repository.BuildSearchQuery(Guid.NewGuid(), [], 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => repository.BuildSearchQuery(Guid.NewGuid(), ["term"], 0));
    }
}
