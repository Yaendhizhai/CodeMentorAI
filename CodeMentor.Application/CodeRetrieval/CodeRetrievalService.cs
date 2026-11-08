using System.Text;
using System.Text.RegularExpressions;
using CodeMentor.Application.Abstractions.Persistence;

namespace CodeMentor.Application.CodeRetrieval;

public sealed class CodeRetrievalService(ICodeChunkRepository codeChunkRepository) : ICodeRetrievalService
{
    public const int MaximumQueryLength = 200;
    public const int MaximumDistinctTerms = 10;
    public const int DefaultResultLimit = 10;
    public const int MaximumResultLimit = 100;

    private static readonly Regex SearchTermPattern = new(
        @"[\p{L}\p{N}]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<IReadOnlyList<CodeChunkSearchResult>> SearchAsync(
        Guid projectId,
        string query,
        int limit = DefaultResultLimit,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("A repository project ID is required.", nameof(projectId));
        }

        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("A non-empty search query is required.", nameof(query));
        }

        if (query.Length > MaximumQueryLength)
        {
            throw new ArgumentException(
                $"The search query cannot exceed {MaximumQueryLength} characters.",
                nameof(query));
        }

        if (limit is < 1 or > MaximumResultLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                $"The result limit must be between 1 and {MaximumResultLimit}.");
        }

        var terms = NormalizeTerms(query);
        if (terms.Count == 0)
        {
            throw new ArgumentException(
                "The search query must contain at least one letter or number.",
                nameof(query));
        }

        if (terms.Count > MaximumDistinctTerms)
        {
            throw new ArgumentException(
                $"The search query cannot contain more than {MaximumDistinctTerms} distinct terms.",
                nameof(query));
        }

        return await codeChunkRepository.SearchAsync(
            projectId,
            terms,
            limit,
            cancellationToken);
    }

    private static IReadOnlyList<string> NormalizeTerms(string query)
    {
        var normalizedQuery = query.Normalize(NormalizationForm.FormKC);
        var terms = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in SearchTermPattern.Matches(normalizedQuery))
        {
            var term = match.Value.ToLowerInvariant();
            if (seen.Add(term))
            {
                terms.Add(term);
            }
        }

        return terms;
    }
}
