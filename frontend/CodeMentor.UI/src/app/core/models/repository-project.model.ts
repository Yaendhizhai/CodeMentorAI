export interface RepositoryProject {
  readonly id: string;
  readonly name: string;
  readonly repositoryUrl: string | null;
  readonly description: string | null;
  readonly createdAtUtc: string;
  readonly updatedAtUtc: string;
}

export interface RegisterRepositoryProjectRequest {
  readonly name: string;
  readonly repositoryUrl: string;
  readonly description: string | null;
}

export interface RepositoryIngestionResponse {
  readonly analysisSessionId: string;
  readonly repositoryProjectId: string;
  readonly treeSha: string;
  readonly fileCount: number;
  readonly chunkCount: number;
  readonly completedAtUtc: string;
}

export interface ApiProblemDetails {
  readonly title?: string;
  readonly detail?: string;
  readonly errors?: Readonly<Record<string, readonly string[]>>;
}
