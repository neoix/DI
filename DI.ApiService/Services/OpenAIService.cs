using Azure.AI.OpenAI;
using Azure.Identity;
using DI.ApiService.Models;
using OpenAI.Chat;
using System.Text.Json;

namespace DI.ApiService.Services;

public class OpenAIService
{
  private readonly AppSettingsModel appSettings;

  private readonly ClientSecretCredential credential;

  private readonly AzureOpenAIClient openAIClient;

  public OpenAIService(IConfiguration config)
  {
    appSettings = config.Get<AppSettingsModel>()!;
    credential = new ClientSecretCredential(
        appSettings.AzureAD.TenantId,
        appSettings.AzureAD.ClientId,
        appSettings.AzureAD.ClientSecret
    );
    openAIClient = new AzureOpenAIClient(new Uri(appSettings.OpenAI.Endpoint), credential);
  }

  public async Task<string> Chat(string userMessage, string? systemMessage = "")
  {
    var chatClient = openAIClient.GetChatClient(appSettings.OpenAI.Deployment);
    var messages = new List<ChatMessage>
    {
      ChatMessage.CreateSystemMessage(systemMessage),
      ChatMessage.CreateUserMessage(userMessage)
    };
    var options = new ChatCompletionOptions
    {
      Temperature = 0.2f
    };
    var response = await chatClient.CompleteChatAsync(messages, options);
    return string.Concat(response.Value.Content.Select(c => c.Text)).Trim();
  }

  public async Task<List<ChunkModel>> EmbedChunksAsync(List<string> chunks)
  {    
    var embeddingClient = openAIClient.GetEmbeddingClient(appSettings.OpenAI.EmbeddingDeployment);

    var response = await embeddingClient.GenerateEmbeddingsAsync(chunks);

    var embeddedChunks = new List<ChunkModel>();

    for (int i = 0; i < response.Value.Count; i++)
    {
      embeddedChunks.Add(new ChunkModel
      {
        Text = chunks[i],
        Embedding = response.Value[i].ToFloats().ToArray()
      });
    }

    return embeddedChunks;
  }

  public async Task<string> ExtractEntitiesAsync(string context, JsonElement schema)
  {
    var chatClient = openAIClient.GetChatClient(appSettings.OpenAI.Deployment);

    var extractTool = ChatTool.CreateFunctionTool(
      functionName: "extract_entities",
      functionDescription: "Extracts structured attributes from the document",
      functionParameters: BinaryData.FromObjectAsJson(schema)
    );

    var systemMessage = """
      You are a document extraction assistant.

      Your task is to extract structured data strictly according to the provided JSON schema.

      Rules:
      - Always respond by calling the extract_entities function.
      - Never answer directly.
      - Only extract values that are explicitly present in the provided text.
      - Do NOT infer, guess, or hallucinate missing values.
      - If a required field is not found in the text, return it as null.
      - If the schema expects an array and no matching items are found, return an empty array [].
      - If the schema expects an object and no matching fields are found, return an empty object {}.
      - Always return valid JSON that matches the schema exactly.
    """;

    var messages = new List<ChatMessage>
    {
      ChatMessage.CreateSystemMessage(systemMessage),
      ChatMessage.CreateUserMessage($"Extract attributes from the following text:\n{context}")
    };

    var options = new ChatCompletionOptions
    {
      Tools = { extractTool },
      Temperature = 0,
      ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
    };

    var response = await chatClient.CompleteChatAsync(messages, options);

    if (response.Value.ToolCalls.Count > 0)
    {
      var toolCall = response.Value.ToolCalls[0];

      return toolCall?.FunctionArguments?.ToString() ?? "{}";
    }
    return "{}";
  }

}
