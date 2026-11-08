export interface CodeSearchRequest {
  readonly query: string;
  readonly limit: number;
}

export interface CodeChunkSearchResult {
  readonly relativePath: string;
  readonly chunkIndex: number;
  readonly startLine: number;
  readonly endLine: number;
  readonly content: string;
  readonly matchScore: number;
}
