namespace CodeMentor.Application.RepositoryIngestion;

public interface IRepositoryIngestionService
{
    Task<RepositoryIngestionResponse> IngestAsync(
        Guid repositoryProjectId,
        CancellationToken cancellationToken = default);
}
