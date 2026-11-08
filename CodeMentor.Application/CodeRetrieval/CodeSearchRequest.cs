using System.ComponentModel.DataAnnotations;

namespace CodeMentor.Application.CodeRetrieval;

/// <summary>Request data for project-scoped lexical code search.</summary>
public sealed record CodeSearchRequest
{
    /// <summary>The source-code terms to search for.</summary>
    [Required]
    public required string Query { get; init; }

    /// <summary>The maximum number of matching chunks to return.</summary>
    public int Limit { get; init; } = 10;
}
