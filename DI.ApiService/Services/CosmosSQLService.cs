namespace DI.ApiService.Services;

using Azure.Core.Serialization;
using Azure.Identity;
using DI.ApiService.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

public class CosmosSQLService
{
  private readonly AppSettingsModel appSettings;

  private readonly ClientSecretCredential credential;

  private readonly CosmosClient cosmosClient;

  private readonly string databaseName;

  public CosmosSQLService(IConfiguration config)
  {
    appSettings = config.Get<AppSettingsModel>()!;
    credential = new ClientSecretCredential(
        appSettings.AzureAD.TenantId,
        appSettings.AzureAD.ClientId,
        appSettings.AzureAD.ClientSecret
    );

    databaseName = appSettings.CosmosSQL.DatabaseName;
    cosmosClient = new CosmosClient(appSettings.CosmosSQL.Endpoint, credential, new CosmosClientOptions
    {
      AllowBulkExecution = true,
      Serializer = new CosmosSystemTextJsonSerializer()
    });

  }

  private Container GetContainer(string containerName)
  {
    return cosmosClient.GetContainer(databaseName, containerName);
  }

  public async Task UpsertAsync<T>(string containerName, T item, string partitionKey)
  {
    var container = GetContainer(containerName);
    await container.UpsertItemAsync(item, new PartitionKey(partitionKey));
  }

  public async Task<T?> GetItemAsync<T>(string containerName, string id, string partitionKey)
  {
    var container = GetContainer(containerName);
    try
    {
      var response = await container.ReadItemAsync<T>(id, new PartitionKey(partitionKey));
      return response.Resource;
    }
    catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
      return default;
    }
  }

  public async Task<List<T>> GetItemsAsync<T>(string containerName, string sqlQuery, Dictionary<string, object>? parameters = null, int maxItems = 100)
  {
    var container = GetContainer(containerName);

    var queryDefinition = new QueryDefinition(sqlQuery);
    if (parameters != null)
    {
      foreach (var kv in parameters)
      {
        // QueryDefinition parameters must be prefixed with @ when used in SQL, callers should use parameter names without @
        queryDefinition = queryDefinition.WithParameter($"@{kv.Key}", kv.Value);
      }
    }

    var iterator = container.GetItemQueryIterator<T>(queryDefinition, requestOptions: new QueryRequestOptions { MaxItemCount = maxItems });
    var results = new List<T>();
    while (iterator.HasMoreResults)
    {
      var response = await iterator.ReadNextAsync();
      results.AddRange(response.Resource);
    }

    return results;
  }
}

public class CosmosSystemTextJsonSerializer : CosmosSerializer
{
  private readonly JsonObjectSerializer _serializer;

  public CosmosSystemTextJsonSerializer()
  {
    _serializer = new JsonObjectSerializer(new JsonSerializerOptions
    {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      WriteIndented = false
    });
  }

  public override T FromStream<T>(Stream stream)
  {
    using (stream)
    {
      if (stream.CanSeek && stream.Length == 0)
        return default!;

      return (T)_serializer.Deserialize(stream, typeof(T), default)!;
    }
  }

  public override Stream ToStream<T>(T input)
  {
    var ms = new MemoryStream();
    _serializer.Serialize(ms, input, typeof(T), default);
    ms.Position = 0;
    return ms;
  }
}
