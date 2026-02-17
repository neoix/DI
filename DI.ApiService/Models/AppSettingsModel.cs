namespace DI.ApiService.Models;

public record AppSettingsModel
{
  public AzureAD AzureAD { get; set; } = default!;

  public StorageAccount StorageAccount { get; set; } = default!;

  public ServiceBus ServiceBus { get; set; } = default!;

  public DocumentIntelligence DocumentIntelligence { get; set; } = default!;

  public AISearch AISearch { get; set; } = default!;

  public OpenAI OpenAI { get; set; } = default!;

  public CosmosSQL CosmosSQL { get; set; } = default!;
}


public record AzureAD(string TenantId, string ClientId, string ClientSecret);

public record StorageAccount(string BlobEndpoint);

public record ServiceBus(string Endpoint);

public record DocumentIntelligence(string Endpoint, string ContentAnalyzer);

public record AISearch(string Endpoint, string IndexName);

public record OpenAI(string Endpoint, string Deployment, string EmbeddingDeployment);

public record CosmosSQL(string Endpoint, string DatabaseName);