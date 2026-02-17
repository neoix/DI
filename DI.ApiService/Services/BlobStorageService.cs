using Azure.Identity;
using Azure.Storage.Blobs;
using DI.ApiService.Models;

namespace DI.ApiService.Services;

public class BlobStorageService
{
  private readonly AppSettingsModel appSettings;

  private readonly ClientSecretCredential credential;

  private readonly BlobServiceClient blobServiceClient;

  public BlobStorageService(IConfiguration config)
  {
    appSettings = config.Get<AppSettingsModel>()!;
    credential = new ClientSecretCredential(
        appSettings.AzureAD.TenantId,
        appSettings.AzureAD.ClientId,
        appSettings.AzureAD.ClientSecret
    );
    blobServiceClient = new BlobServiceClient(new Uri(appSettings.StorageAccount.BlobEndpoint), credential);
  }

  public async Task<string> SaveBlobAsync(string containerName, string blobName, Stream data)
  {    
    var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
    await containerClient.CreateIfNotExistsAsync();
    var blobClient = containerClient.GetBlobClient(blobName);
    await blobClient.UploadAsync(data, overwrite: true);
    return blobClient.Uri.ToString();
  }

  public async Task<Stream> GetBlobStreamAsync(string blobUri)
  {
    var blobClient = new BlobClient(new Uri(blobUri), credential);
    return await blobClient.OpenReadAsync();
    //var blob = await blobClient.DownloadContentAsync();
    //var bytes = blob.Value.Content.ToArray();
    //return new MemoryStream(bytes);
  }
}
