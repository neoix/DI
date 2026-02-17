using DI.ApiService.Handlers;
using DI.ApiService.Models;
using DI.ApiService.Services;
using DI.ApiService.Workers;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Configure logging
builder.Logging.AddConsole();

// Register application services
builder.Services.AddSingleton<BlobStorageService>();
builder.Services.AddSingleton<ServiceBusService>();
builder.Services.AddSingleton<DocumentIntelligenceService>();
builder.Services.AddSingleton<OpenAIService>();
builder.Services.AddSingleton<AISearchService>();
builder.Services.AddSingleton<CosmosSQLService>();
builder.Services.AddSingleton<RedisCacheService>();

// Register background worker
builder.Services.AddHostedService<IngestionWorker>();
builder.Services.AddHostedService<ExtractionWorker>();

// Register document handler
builder.Services.AddScoped<DocumentHandler>();
builder.Services.AddScoped<JobWorkerHandler>();

builder.WebHost.ConfigureKestrel(options =>
{
  options.Limits.MaxRequestBodySize = 100_000_000; // 100 MB
});

builder.Services.Configure<FormOptions>(options =>
{
  options.MultipartBodyLengthLimit = 100_000_000; // 100 MB
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Upload file endpoint
app.MapPost("/upload", async (
  HttpRequest request, 
  [FromServices] DocumentHandler documentHandler, 
  [FromServices] ILogger<Program> logger) =>
{
  var form = await request.ReadFormAsync();
  var file = form.Files.Count > 0 ? form.Files[0] : null;

  if (file == null || file.Length == 0)
  {
    logger.LogWarning("No file uploaded in the request.");
    return Results.BadRequest("No file uploaded.");
  }

  var response = await documentHandler.UploadFileAsync(file);
  return Results.Ok(response);
})
.WithName("UploadFile");

// Search endpoint
app.MapPost("/search/{documentId}", async (
  [FromRoute] string documentId,
  [FromBody] SearchRequestModel request,
  [FromServices] DocumentHandler documentHandler) =>
{
  var response = await documentHandler.SearchAsync(documentId, request);
  return Results.Ok(response);
});

// Extract endpoint
app.MapPost("/extract/{documentId}", async (
  [FromRoute] string documentId,
  [FromBody] ExtractionRequestModel request,
  [FromServices] DocumentHandler documentHandler) =>
{
  var response = await documentHandler.ExtractAsync(documentId, request);
  return Results.Content(response, "application/json");
});

// Extraction bulk endpoint
app.MapPost("extract/bulk/{documentId}", async (
  [FromRoute] string documentId,
  [FromBody] List<ExtractionRequestModel> request,
  [FromServices] DocumentHandler documentHandler) =>
{
  var response = await documentHandler.ExtractBulkAsync(documentId, request);
  return Results.Ok(response);
});

// Job status endpoint
app.MapGet("/status/{id}", async (
  [FromRoute] string id,
  [FromServices] JobWorkerHandler jobHandler) =>
{  
  var status = await jobHandler.GetJobStatusAsync(id);
  return Results.Ok(status);
});

app.MapDefaultEndpoints();

app.Run();