namespace DI.ApiService.Models;

public record SearchRequestModel(string Query, bool UseSynonyms, int TopK = 5);
