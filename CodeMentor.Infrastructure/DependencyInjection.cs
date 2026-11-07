using CodeMentor.Application.Abstractions.Persistence;
using CodeMentor.Application.GitHub;
using CodeMentor.Infrastructure.GitHub;
using CodeMentor.Infrastructure.Persistence;
using CodeMentor.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CodeMentor.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<CodeMentorDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                sqlServerOptions => sqlServerOptions.MigrationsAssembly("CodeMentor.Infrastructure")));
        services.TryAddSingleton<GitHubRepositoryTreeLimits>();
        services.TryAddSingleton<GitHubRepositoryFileContentLimits>();
        services.TryAddSingleton<GitHubRepositoryFileSelectionPolicy>(
            _ => new GitHubRepositoryFileSelectionPolicy());
        services.AddHttpClient<IGitHubRepositoryClient, GitHubRepositoryClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<CodeMentorDbContext>());
        services.AddScoped<IRepositoryProjectRepository, RepositoryProjectRepository>();
        services.AddScoped<IRepositoryFileRepository, RepositoryFileRepository>();
        services.AddScoped<ICodeChunkRepository, CodeChunkRepository>();
        services.AddScoped<IAnalysisSessionRepository, AnalysisSessionRepository>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IConversationMessageRepository, ConversationMessageRepository>();

        return services;
    }
}
