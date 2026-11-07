using System.ComponentModel.DataAnnotations;

namespace CodeMentor.Application.RepositoryProjects;

/// <summary>Request data for registering a repository project.</summary>
public sealed record RegisterRepositoryProjectRequest : IValidatableObject
{
    /// <summary>The display name of the repository project.</summary>
    [Required, StringLength(200, MinimumLength = 1)]
    public required string Name { get; init; }

    /// <summary>The optional remote repository URL.</summary>
    [StringLength(2048)]
    public string? RepositoryUrl { get; init; }

    /// <summary>An optional description of the repository project.</summary>
    [StringLength(2000)]
    public string? Description { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RepositoryUrl is not null && string.IsNullOrWhiteSpace(RepositoryUrl))
        {
            yield return new ValidationResult(
                "Repository URL cannot be empty when provided.",
                [nameof(RepositoryUrl)]);
        }

        if (Description is not null && string.IsNullOrWhiteSpace(Description))
        {
            yield return new ValidationResult(
                "Description cannot be empty when provided.",
                [nameof(Description)]);
        }
    }
}
