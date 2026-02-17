var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.DI_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");

builder.Build().Run();
