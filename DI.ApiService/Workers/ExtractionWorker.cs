using DI.ApiService.Extensions;
using DI.ApiService.Handlers;
using DI.ApiService.Models;
using DI.ApiService.Services;

namespace DI.ApiService.Workers;

public class ExtractionWorker(ServiceBusService serviceBusService,IServiceScopeFactory scopeFactory, ILogger<ExtractionWorker> logger) : IHostedService
{
  private Task? backgroundTask;
  private CancellationTokenSource? cts;
  private readonly string queueName = "extraction-jobs";

  public Task StartAsync(CancellationToken cancellationToken)
  {
    logger.LogInformation($"{nameof(ExtractionWorker)} is started.");
    cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    backgroundTask = Task.Run(() => RunAsync(cts.Token), cancellationToken);
    return Task.CompletedTask;
  }

  public async Task StopAsync(CancellationToken cancellationToken)
  {
    logger.LogInformation($"{nameof(ExtractionWorker)} is stopped.");
    cts?.Cancel();
    if (backgroundTask != null)
    {
      try
      {
        await backgroundTask;
      }
      catch (TaskCanceledException) { }
    }
  }

  private async Task RunAsync(CancellationToken cancellationToken)
  {    
    while (!cancellationToken.IsCancellationRequested)
    {
      try
      {
        using var scope = scopeFactory.CreateScope();
        //var jobHandler = scope.ServiceProvider.GetRequiredService<JobWorkerHandler>();

        var message = await serviceBusService.DequeueAsync(queueName);        

        if (message != null)
        {
          logger.LogInformation("Dequeued message: {Message}", message);
          var job = message.AsObject<ExtractionJobModel>();          

          if (job == null)
          {
            logger.LogWarning("Failed to deserialize message: {Message}", message);
            continue;
          }

          var id = job.ExtractionId;
          try
          {         
            //await jobHandler.SetJobStatusAsync(id, "Processing", string.Empty);
            await ProcessAsync(job);
            //await jobHandler.SetJobStatusAsync(id, "Completed", string.Empty);            
          }
          catch (Exception exception)
          {
            logger.LogError(exception, "Exception occurred while processing message");
            //await jobHandler.SetJobStatusAsync(id, "Failed", exception.Message);
          }
        }
        else
        {
          //logger.LogInformation("No messages in the queue: {QueueName}", queueName);
        }

        await Task.Delay(2000, cancellationToken);
      }
      catch (TaskCanceledException)
      {
        break;
      }
      catch (Exception exception)
      {
        logger.LogError(exception, "Exception in ingestion loop");
      }
    }
  }

  public async Task ProcessAsync(ExtractionJobModel job)
  {
    var startTime = DateTime.Now;
    logger.LogInformation("Processing extraction {ExtractionId} at {Now}", job.ExtractionId, startTime);

    //var documentLogger = loggerFactory.CreateLogger<DocumentHandler>();

    using var scope = scopeFactory.CreateScope();
    var documentHandler = scope.ServiceProvider.GetRequiredService<DocumentHandler>();

    await documentHandler.ExtractAndPersist(job);

    //var documentHandler = new DocumentHandler(blobStorageService, serviceBusService, searchAIService, openAIService, documentIntelligenceService, cosmosSQLService, documentLogger);
    //int total = job.ExtractionRequests.Count;
    //int completed = 0;
    //var result = new List<JsonNode?>();

    //var extractionTasks = job.ExtractionRequests.Select(async request =>
    //{
    //  var extractedJson = await documentHandler.ExtractAsync(job.DocumentId, request);
    //  var node = JsonNode.Parse(extractedJson);
    //  if (node != null)
    //  {
    //    result.Add(node);
    //  }

    //  Interlocked.Increment(ref completed);

    //  logger.LogInformation("Progress: {Completed}/{Total} extractions completed", completed, total);

    //});

    //await Task.WhenAll(extractionTasks);

    //var extraction = result.AsString();

    //// Save to Cosmos DB
    //await cosmosSQLService.UpsertAsync<object>(
    //  "extractions",
    //  new
    //  {
    //    id = job.ExtractionId,
    //    partitionKey = job.DocumentId,
    //    data = result
    //  },
    //  job.DocumentId
    //);

    //// Save to Blob Storage
    //using var finalStream = new MemoryStream(Encoding.UTF8.GetBytes(extraction));
    //var blobUrl = await blobStorageService.SaveBlobAsync("files", $"{job.DocumentId}/{job.ExtractionId}.json", finalStream);

    logger.LogInformation("Processing of extraction {ExtractionId} completed at {Now}", job.ExtractionId, DateTime.Now);
    logger.LogInformation("Total processing time: {Duration} seconds", (DateTime.Now - startTime).TotalSeconds);
  }  
}
