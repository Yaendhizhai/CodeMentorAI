namespace CodeMentor.Application.CodeRetrieval;

public interface ICodeRetrievalService
{
    Task<IReadOnlyList<CodeChunkSearchResult>> SearchAsync(
        Guid projectId,
        string query,
        int limit = 10,
        CancellationToken cancellationToken = default);
}
