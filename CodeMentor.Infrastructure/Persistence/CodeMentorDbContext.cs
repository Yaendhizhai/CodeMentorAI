using CodeMentor.Application.Abstractions.Persistence;
using CodeMentor.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodeMentor.Infrastructure.Persistence;

public sealed class CodeMentorDbContext(DbContextOptions<CodeMentorDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<RepositoryProject> RepositoryProjects => Set<RepositoryProject>();

    public DbSet<RepositoryFile> RepositoryFiles => Set<RepositoryFile>();

    public DbSet<CodeChunk> CodeChunks => Set<CodeChunk>();

    public DbSet<AnalysisSession> AnalysisSessions => Set<AnalysisSession>();

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CodeMentorDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
