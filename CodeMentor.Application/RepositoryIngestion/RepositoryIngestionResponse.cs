namespace CodeMentor.Application.RepositoryIngestion;

public sealed record RepositoryIngestionResponse(
    Guid AnalysisSessionId,
    Guid RepositoryProjectId,
    string TreeSha,
    int FileCount,
    int ChunkCount,
    DateTimeOffset CompletedAtUtc);
