using CodeMentor.Core.Entities;
using CodeMentor.Core.Enums;
using CodeMentor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CodeMentor.Tests;

public sealed class DomainFoundationTests
{
    [Fact]
    public void RepositoryProject_InitializesIdentityAndUtcTimestamps()
    {
        var project = new RepositoryProject("CodeMentor");

        Assert.NotEqual(Guid.Empty, project.Id);
        Assert.Equal(TimeSpan.Zero, project.CreatedAtUtc.Offset);
        Assert.Equal(project.CreatedAtUtc, project.UpdatedAtUtc);
        Assert.Empty(project.Files);
        Assert.Empty(project.AnalysisSessions);
        Assert.Empty(project.Conversations);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void RepositoryProject_RejectsEmptyName(string name)
    {
        Assert.Throws<ArgumentException>(() => new RepositoryProject(name));
    }

    [Fact]
    public void RepositoryFile_RejectsInvalidSizeAndNonRelativePath()
    {
        var projectId = Guid.NewGuid();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RepositoryFile(projectId, "src/File.cs", sizeInBytes: -1));
        Assert.Throws<ArgumentException>(
            () => new RepositoryFile(projectId, "../outside.cs"));
    }

    [Fact]
    public void CodeChunk_RejectsInvalidLineRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CodeChunk(Guid.NewGuid(), chunkIndex: 0, startLine: 0, endLine: 1, "content"));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CodeChunk(Guid.NewGuid(), chunkIndex: 0, startLine: 3, endLine: 2, "content"));
    }

    [Fact]
    public void AnalysisSession_DefaultsToQueued()
    {
        var session = new AnalysisSession(Guid.NewGuid());

        Assert.Equal(AnalysisSessionStatus.Queued, session.Status);
        Assert.Null(session.StartedAtUtc);
        Assert.Null(session.CompletedAtUtc);
    }

    [Fact]
    public void DbContext_MapsAggregateRelationshipsAndUniqueIndexes()
    {
        var options = new DbContextOptionsBuilder<CodeMentorDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=CodeMentorModelTest;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        using var dbContext = new CodeMentorDbContext(options);
        var model = dbContext.Model;
        var fileType = model.FindEntityType(typeof(RepositoryFile));
        var chunkType = model.FindEntityType(typeof(CodeChunk));
        var messageType = model.FindEntityType(typeof(ConversationMessage));

        Assert.NotNull(fileType);
        Assert.NotNull(chunkType);
        Assert.NotNull(messageType);

        Assert.Contains(
            fileType.GetIndexes(),
            index => index.IsUnique &&
                     index.Properties.Select(property => property.Name)
                         .SequenceEqual(new[] { "ProjectId", "RelativePath" }));
        Assert.Contains(
            chunkType.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(RepositoryFile) &&
                          foreignKey.DeleteBehavior == DeleteBehavior.Cascade);
        Assert.Contains(
            messageType.GetIndexes(),
            index => index.IsUnique &&
                     index.Properties.Select(property => property.Name)
                         .SequenceEqual(new[] { "ConversationId", "Sequence" }));
    }
}
