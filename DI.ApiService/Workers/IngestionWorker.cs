using DI.ApiService.Extensions;
using DI.ApiService.Handlers;
using DI.ApiService.Models;
using DI.ApiService.Services;

namespace DI.ApiService.Workers;

public class IngestionWorker(ServiceBusService serviceBusService, IServiceScopeFactory scopeFactory, ILogger<IngestionWorker> logger) : IHostedService
{
  private Task? backgroundTask;
  private CancellationTokenSource? cts;
  private readonly string queueName = "ingestion-jobs";

  public Task StartAsync(CancellationToken cancellationToken)
  {
    logger.LogInformation($"{nameof(IngestionWorker)} is started.");
    cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    backgroundTask = Task.Run(() => RunAsync(cts.Token), cancellationToken);
    return Task.CompletedTask;
  }

  public async Task StopAsync(CancellationToken cancellationToken)
  {
    logger.LogInformation($"{nameof(IngestionWorker)} is stopped.");
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
        //using var scope = scopeFactory.CreateScope();
        //var jobHandler = scope.ServiceProvider.GetRequiredService<JobWorkerHandler>();

        var message = await serviceBusService.DequeueAsync(queueName);        

        if (message != null)
        {
          logger.LogInformation("Dequeued message: {Message}", message);
          var job = message.AsObject<IngestionJobModel>();
          if (job == null)
          {
            logger.LogWarning("Failed to deserialize message: {Message}", message);
            continue;
          }

          var id = job.DocumentId;
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

  public async Task ProcessAsync(IngestionJobModel job)
  {
    var startTime = DateTime.Now;
    logger.LogInformation("Processing document {DocumentId} at {Now}", job.DocumentId, startTime);

    using var scope = scopeFactory.CreateScope();
    var documentHandler = scope.ServiceProvider.GetRequiredService<DocumentHandler>();

    await documentHandler.ChunkAndIndex(job);  

    logger.LogInformation("Processing of document {DocumentId} completed at: {Now}", job.DocumentId, DateTime.Now);
    logger.LogInformation("Total processing time: {Duration} seconds", (DateTime.Now - startTime).TotalSeconds);
  }  
}
