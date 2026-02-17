using DI.ApiService.Extensions;
using DI.ApiService.Models;
using DI.ApiService.Services;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json.Nodes;

namespace DI.ApiService.Handlers;

public class DocumentHandler(BlobStorageService blobStorageService, ServiceBusService serviceBusService, AISearchService searchAIService, OpenAIService openAIService, DocumentIntelligenceService documentIntelligenceService, CosmosSQLService cosmosSQLService, ILogger<DocumentHandler> logger)
{
  public async Task<IngestionJobModel> UploadFileAsync(IFormFile file)
  {
    try
    {
      var documentId = Guid.NewGuid().ToString();

      // Upload document to blob storage
      var blobName = $"{documentId}/{file.FileName}";
      using var stream = file.OpenReadStream();
      var blobUri = await blobStorageService.SaveBlobAsync("files", blobName, stream);
      logger.LogInformation("Uploaded file {FileName} to blob storage at {BlobUri}", file.FileName, blobUri);

      // Queue the ingestion job
      var ingestionJob = new IngestionJobModel(documentId, blobUri);
      var messageBody = ingestionJob.AsString();
      await serviceBusService.EnqueueAsync("ingestion-jobs", messageBody);
      logger.LogInformation("Enqueued ingestion job for document {DocumentId}", documentId);

      return ingestionJob;
    }
    catch (Exception exception)
    {
      logger.LogError(exception, "Error uploading file");
      throw;
    }       
  }

  public async Task<IEnumerable<object>> SearchAsync(string documentId, SearchRequestModel request)
  {
    try
    {
      var response = await searchAIService.RunSemanticHybridSearch(documentId, request);

      var results = response.Select(r => new
      {
        r.Score,
        Id = r.Document["id"],
        DocumentId = documentId,
        Text = r.Document["text"],
        PageNumbers = r.Document["pageNumbers"],
        MetdaDataBlobUrl = r.Document["metadataBlobUrl"],
        Captions = r.SemanticSearch.Captions?.Select(c => c.Text),

      });

      logger.LogInformation("Search for query '{Query}' returned {ResultCount} results", request.Query, results.Count());
      return results;
    }
    catch (Exception exception)
    {
      logger.LogError(exception, "Error performing search");
      throw;
    }    
  }

  public async Task<string> ExtractAsync(string documentId, ExtractionRequestModel request)
  {
    try
    {
      var response = await searchAIService.RunSemanticHybridSearch(documentId, request.SearchRequest);

      double maxScore = response.Max(x => x.Score) ?? 0;

      // Build tasks first
      var tasks = response.Select(async r =>
      {
        string text = r.Document["text"]?.ToString() ?? "";

        string pages = r.Document.TryGetValue("pageNumbers", out var p)
            ? string.Join(", ", ((IEnumerable<object>)p).Select(x => x.ToString()))
            : "";

        string metadataBlobUrl = r.Document.TryGetValue("metadataBlobUrl", out var b)
            ? b?.ToString() ?? ""
            : "";

        double? confidence = maxScore > 0 ? r.Score / maxScore : 0;

        return
            $"[Pages: {pages}] " +
            $"[Confidence: {confidence:F2}] " +
            $"[MetaDataBlobUrl: {metadataBlobUrl}]\n" +
            $"{text}\n";
      });

      // Execute all tasks in parallel
      var results = await Task.WhenAll(tasks);

      // Join into final context
      string context = string.Join("\n\n", results);

      logger.LogInformation(context);
      string extractedJson = await openAIService.ExtractEntitiesAsync(context, request.Schema);
      logger.LogInformation("Extraction for query '{Query}' returned JSON of length {JsonLength}", request.SearchRequest.Query, extractedJson.Length);

      return extractedJson;
    }
    catch (Exception exception)
    {
      logger.LogError(exception, "Error performing extraction");
      throw new Exception(exception.Message);
    }
  }

  public async Task<ExtractionResponseModel> ExtractBulkAsync(string documentId, List<ExtractionRequestModel> requests)
  {
    try
    {
      var extractionId = Guid.NewGuid().ToString();     
      var job = new ExtractionJobModel(extractionId, documentId, requests);
      var messageBody = job.AsString();
      await serviceBusService.EnqueueAsync("extraction-jobs", messageBody);
      logger.LogInformation("Enqueued extraction job {ExtractionId} with {RequestCount} requests", extractionId, requests.Count);
      return new(extractionId);
    }
    catch (Exception exception)
    {
      logger.LogError(exception, "Error enqueuing bulk extraction");
      throw;
    }
  }

  public async Task<List<ChunkModel>> ChunkAndIndex(IngestionJobModel job)
  {
    using var stream = await blobStorageService.GetBlobStreamAsync(job.BlobUri);

    // Get chunks from document intelligence
    logger.LogInformation("Chunking document {DocumentId} from blob URI {BlobUri}", job.DocumentId, job.BlobUri);
    var chunks = await documentIntelligenceService.ChunkAsync(stream);
    logger.LogInformation("Chunks count for document {DocumentId}: {ChunkCount}", job.DocumentId, chunks.Count);

    // Save chunks to Cosmos DB
    logger.LogInformation("Saving chunks for document {DocumentId} to Cosmos DB", job.DocumentId);
    var cosmosTask = chunks.Select(chunk =>
      cosmosSQLService.UpsertAsync(
        "metadata",
        new
        {
          id = chunk.ChunkId,
          partitionKey = job.DocumentId,
          data = chunk
        },
        job.DocumentId
      )
    );

    await Task.WhenAll(cosmosTask);

    // Index chunks with AI Search
    logger.LogInformation("Indexing chunks for document {DocumentId}", job.DocumentId);
    await searchAIService.IndexChunksAsync(job.DocumentId, chunks);

    return chunks;
  }

  public async Task ExtractAndPersist(ExtractionJobModel job)
  {    
    int total = job.ExtractionRequests.Count;
    int completed = 0;
    var result = new List<JsonNode?>();

    logger.LogInformation("Starting extraction for job {ExtractionId} with {RequestCount} requests", job.ExtractionId, total);
    var extractionTasks = job.ExtractionRequests.Select(async request =>
    {
      var extractedJson = await ExtractAsync(job.DocumentId, request);
      var node = JsonNode.Parse(extractedJson);
      if (node != null)
      {
        result.Add(node);
      }

      Interlocked.Increment(ref completed);

      logger.LogInformation("Extractions {Completed}/{Total} completed", completed, total);
    });

    await Task.WhenAll(extractionTasks);

    var extraction = result.AsString();

    // Save the extraction to Cosmos DB
    logger.LogInformation("Saving extraction result for job {ExtractionId} to Cosmos DB", job.ExtractionId);
    await cosmosSQLService.UpsertAsync<object>(
      "extractions",
      new
      {
        id = job.ExtractionId,
        partitionKey = job.DocumentId,
        data = result
      },
      job.DocumentId
    );

    // Save extraction to Blob Storage
    logger.LogInformation("Saving extraction result for job {ExtractionId} to Blob Storage", job.ExtractionId);
    using var finalStream = new MemoryStream(Encoding.UTF8.GetBytes(extraction));
    var blobUrl = await blobStorageService.SaveBlobAsync("files", $"{job.DocumentId}/{job.ExtractionId}.json", finalStream);
  }

}
