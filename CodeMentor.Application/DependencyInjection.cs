using CodeMentor.Application.RepositoryProjects;
using CodeMentor.Application.RepositoryIngestion;
using Microsoft.Extensions.DependencyInjection;

namespace CodeMentor.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IRepositoryProjectService, RepositoryProjectService>();
        services.AddScoped<IRepositoryIngestionService, RepositoryIngestionService>();
        return services;
    }
}
