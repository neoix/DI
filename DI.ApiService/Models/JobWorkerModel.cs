namespace DI.ApiService.Models;

public record IngestionJobModel(string DocumentId, string BlobUri);

public record ExtractionJobModel(string ExtractionId, string DocumentId, List<ExtractionRequestModel> ExtractionRequests);

public record JobStatusModel(string Status, string? Error);