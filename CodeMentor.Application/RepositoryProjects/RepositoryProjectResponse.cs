namespace CodeMentor.Application.RepositoryProjects;

/// <summary>Repository project details returned by the API.</summary>
public sealed record RepositoryProjectResponse(
    Guid Id,
    string Name,
    string? RepositoryUrl,
    string? Description,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
