using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using DI.ApiService.Extensions;
using DI.ApiService.Models;
using System.Text;

namespace DI.ApiService.Services;

public class AISearchService
{
  private readonly AppSettingsModel appSettings;

  private readonly ClientSecretCredential credential;

  private readonly SearchClient searchClient;

  private readonly BlobStorageService blobStorageService;

  private readonly OpenAIService openAIService;

  public AISearchService(IConfiguration config, BlobStorageService blobStorageService, OpenAIService openAIService)
  {
    appSettings = config.Get<AppSettingsModel>()!;
    credential = new ClientSecretCredential(
        appSettings.AzureAD.TenantId,
        appSettings.AzureAD.ClientId,
        appSettings.AzureAD.ClientSecret
    );
    searchClient = new SearchClient(new Uri(appSettings.AISearch.Endpoint), appSettings.AISearch.IndexName, credential);
    this.blobStorageService = blobStorageService;
    this.openAIService = openAIService;
  }

  public SearchClient SearchClient => searchClient;

  public async Task IndexChunksAsync(string documentId, List<ChunkModel> chunks)
  {
    var docs = new List<SearchDocument>();

    foreach (var chunk in chunks)
    {
      //// Upload bounding box metadata to Blob Storage
      //using var metadataBlob = new MemoryStream(Encoding.UTF8.GetBytes(chunk.AsString()));
      //string metadataBlobUrl = await blobStorageService.SaveBlobAsync("files", $"{documentId}/metadata/chunk-{chunk.ChunkId}.json", metadataBlob);

      // Build the lightweight Search document
      var searchDocument = new SearchDocument
      {
        ["id"] = chunk.ChunkId,
        ["documentId"] = documentId,
        ["text"] = chunk.Text,
        ["pageNumbers"] = chunk.PageNumbers
        //["metadataBlobUrl"] = metadataBlobUrl
      };

      docs.Add(searchDocument);
    }

    // Send all docs in one request
    var result = await searchClient.IndexDocumentsAsync(IndexDocumentsBatch.Upload(docs));

    // Log failures
    foreach (var r in result.Value.Results.Where(r => !r.Succeeded))
    {
      Console.WriteLine($"Failed to index chunk {r.Key}: {r.ErrorMessage}");
    }
  }

  public async Task<IReadOnlyList<SearchResult<SearchDocument>>> RunSemanticHybridSearch(string documentId, SearchRequestModel searchRequestModel)
  {
    //var deploymentName = config["AzureOpenAIEmbeddingDeployment"]!;

    //var embeddingClient = openAIService.AzureOpenAIClient.GetEmbeddingClient(deploymentName);

    //var embeddingResult = await embeddingClient.GenerateEmbeddingsAsync([searchRequestModel.Query]);
    //var queryVector = embeddingResult.Value[0].ToFloats().ToArray();

    var options = new SearchOptions
    {
      Size = searchRequestModel.TopK,
      QueryType = SearchQueryType.Semantic,
      
      //Select = { "documentId", "content" },
      //VectorSearch = new()
      //{
      //  Queries =
      //    {
      //      new VectorizedQuery(queryVector)
      //      {
      //          KNearestNeighborsCount = searchRequestModel.TopK,
      //          Fields = { "embedding" }
      //      }
      //    }
      //},
      SemanticSearch = new SemanticSearchOptions
      {
        SemanticConfigurationName = "default",
        QueryCaption = new(QueryCaptionType.Extractive),
        QueryAnswer = new(QueryAnswerType.Extractive)
        {
          Count = searchRequestModel.TopK
        },
      }
    };

    if (!string.IsNullOrWhiteSpace(documentId))
    {
      options.Filter = $"documentId eq '{documentId}'";
    }

    var query = searchRequestModel.Query;
    if (searchRequestModel.UseSynonyms)
    {
      var prompt = $@"
        Generate 3–6 synonyms or equivalent phrases for the following search query.
        Return ONLY a comma-separated list. No explanations.

        Query: {searchRequestModel.Query}
      ";
      var synonyms = await openAIService.Chat(prompt);
      query = $"{searchRequestModel.Query}. Related terms: {synonyms}.";
    }

    var response = await searchClient.SearchAsync<SearchDocument>(query, options);
    return [.. response.Value.GetResults()];
  }
}
