using DI.ApiService.Extensions;
using DI.ApiService.Models;
using DI.ApiService.Services;

namespace DI.ApiService.Handlers;

public class JobWorkerHandler(RedisCacheService redisCacheService)
{
  public async Task<JobStatusModel> GetJobStatusAsync(string id)
  {
    var exists = await redisCacheService.ExistsAsync(id);
    if (exists)
    {
      var response = await redisCacheService.GetAsync(id);
      if (response == null)
      {
        return new("Not found", $"{id} is not found");
      }
      var jobStatus = response.AsObject<JobStatusModel>();
      return jobStatus!;
    }
    return new("Not found", $"{id} is not found");
  }

  public async Task SetJobStatusAsync(string documentId, string status, string? error = null)
  {
    var jobStatus = new JobStatusModel(status, error);
    await redisCacheService.SetAsync(documentId, jobStatus.AsString());
  }
}
