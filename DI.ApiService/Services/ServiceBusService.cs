using Azure.Identity;
using Azure.Messaging.ServiceBus;
using DI.ApiService.Models;

namespace DI.ApiService.Services;

public class ServiceBusService
{
  private readonly AppSettingsModel appSettings;

  private readonly ClientSecretCredential credential;

  private readonly ServiceBusClient serviceBusClient;

  public ServiceBusService(IConfiguration config)
  {
    appSettings = config.Get<AppSettingsModel>()!;
    credential = new ClientSecretCredential(
      appSettings.AzureAD.TenantId,
      appSettings.AzureAD.ClientId,
      appSettings.AzureAD.ClientSecret
    );
    serviceBusClient = new ServiceBusClient(appSettings.ServiceBus.Endpoint, credential);
  }

  public async Task EnqueueAsync(string queueName, string messageBody)
  {
    var sender = serviceBusClient.CreateSender(queueName);

    var message = new ServiceBusMessage(messageBody)
    {
      ContentType = "text/plain",
      ApplicationProperties = { ["documentId"] = Guid.NewGuid().ToString() }
    };

    await sender.SendMessageAsync(message);
    await sender.DisposeAsync();
  }

  public async Task<string?> DequeueAsync(string queueName)
  {
    var receiver = serviceBusClient.CreateReceiver(queueName);
    var message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5));
    if (message == null)
    {
      return null;
    }

    var body = message.Body.ToString();
    await receiver.CompleteMessageAsync(message);
    return body;
  }
}
