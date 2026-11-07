using CodeMentor.Application.RepositoryProjects;
using Microsoft.Extensions.DependencyInjection;

namespace CodeMentor.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IRepositoryProjectService, RepositoryProjectService>();
        return services;
    }
}
