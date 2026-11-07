using CodeMentor.Application.Abstractions.Persistence;
using CodeMentor.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Runtime.ExceptionServices;

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

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        IDbContextTransaction? transaction = null;
        Exception? failure = null;

        try
        {
            transaction = await Database.BeginTransactionAsync(cancellationToken);
            await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            failure = exception;
            if (transaction is not null)
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                catch (Exception rollbackException)
                {
                    failure = new AggregateException(
                        "The ingestion transaction failed and rollback also failed.",
                        exception,
                        rollbackException);
                }
            }

            ChangeTracker.Clear();
        }

        if (transaction is not null)
        {
            try
            {
                await transaction.DisposeAsync();
            }
            catch (Exception disposeException)
            {
                ChangeTracker.Clear();
                failure = failure is null
                    ? disposeException
                    : new AggregateException(
                        "The transaction failed and its transaction could not be disposed cleanly.",
                        failure,
                        disposeException);
            }
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
