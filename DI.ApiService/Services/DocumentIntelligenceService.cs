using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Identity;
using DI.ApiService.Models;
using System.Text;

namespace DI.ApiService.Services;

public class DocumentIntelligenceService
{
  private readonly AppSettingsModel appSettings;

  private readonly ClientSecretCredential credential;

  private readonly DocumentIntelligenceClient documentIntelligenceClient;

  public DocumentIntelligenceService(IConfiguration config)
  {
    appSettings = config.Get<AppSettingsModel>()!;
    credential = new ClientSecretCredential(
        appSettings.AzureAD.TenantId,
        appSettings.AzureAD.ClientId,
        appSettings.AzureAD.ClientSecret
    );
    documentIntelligenceClient = new DocumentIntelligenceClient(new Uri(appSettings.DocumentIntelligence.Endpoint), credential);
  }

  public async Task<List<ChunkModel>> ChunkAsync(Stream stream)
  {
    var poller = await documentIntelligenceClient.AnalyzeDocumentAsync(
        WaitUntil.Completed,
        appSettings.DocumentIntelligence.ContentAnalyzer,
        BinaryData.FromStream(stream)
    );

    var result = poller.Value;

    var chunks = new List<ChunkModel>();
    var chunk = new ChunkModel();
    var buffer = new StringBuilder();
    int wordCount = 0;
    int chunkIndex = 1;

    const int maxWords = 250;

    foreach (var page in result.Pages)
    {
      bool pageWordsAdded = false;

      foreach (var line in page.Lines)
      {
        if (string.IsNullOrWhiteSpace(line.Content))
          continue;

        int lineWordCount = line.Content
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Length;

        // If adding this line exceeds the limit → start a new chunk
        if (wordCount + lineWordCount > maxWords)
        {
          chunk.ChunkId = chunkIndex.ToString();
          chunk.Text = buffer.ToString().Trim();
          chunks.Add(chunk);

          chunkIndex++;
          chunk = new ChunkModel();
          buffer.Clear();
          wordCount = 0;
          pageWordsAdded = false; // allow adding page words again for new chunk
        }

        // Add line text
        buffer.AppendLine(line.Content);
        wordCount += lineWordCount;

        // Add paragraph metadata (line-level)
        chunk.Metadata.Paragraphs.Add(new ParagraphMetadata(
            line.Content,
            page.PageNumber,
            line.Polygon
        ));

        // Track page numbers
        if (!chunk.PageNumbers.Contains(page.PageNumber))
          chunk.PageNumbers.Add(page.PageNumber);


        // Add page words ONCE per chunk
        if (!pageWordsAdded)
        {
          foreach (var word in page.Words)
          {
            chunk.Metadata.Words.Add(new WordMetadata(
                word.Content,
                page.PageNumber,
                word.Polygon
            ));
          }
          pageWordsAdded = true;
        }
      }
    }

    // Final chunk
    if (buffer.Length > 0)
    {
      chunk.ChunkId = chunkIndex.ToString();
      chunk.Text = buffer.ToString().Trim();
      chunks.Add(chunk);
    }

    return chunks;
  }
}
