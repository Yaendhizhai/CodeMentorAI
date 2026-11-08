namespace CodeMentor.Application.CodeRetrieval;

public sealed record CodeChunkSearchResult(
    string RelativePath,
    int ChunkIndex,
    int StartLine,
    int EndLine,
    string Content,
    int MatchScore);
