using System.Text.Json;

namespace DI.ApiService.Models;

public record ExtractionRequestModel(SearchRequestModel SearchRequest, JsonElement Schema);

public record ExtractionResponseModel(string ExtractionId);