namespace DI.ApiService.Models;

public record ChunkModel
{
  public string ChunkId { get; set; } = string.Empty;

  public string Text { get; set; } = string.Empty;

  public IReadOnlyList<float> Embedding { get; set; } = [];

  public List<int> PageNumbers { get; set; } = [];

  public ChunkMetadata Metadata { get; set; } = new();
}

public class ChunkMetadata
{
  public List<ParagraphMetadata> Paragraphs { get; set; } = [];

  public List<WordMetadata> Words { get; set; } = [];
}

public record ParagraphMetadata(string Content, int PageNumber, IReadOnlyList<float> Polygon);

public record WordMetadata(string Content, int PageNumber, IReadOnlyList<float> Polygon);

public record EmbeddingResponse(List<EmbeddingData> Data);

public record EmbeddingData(List<float> Embedding);