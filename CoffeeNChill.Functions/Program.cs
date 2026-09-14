using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Build and initialize the isolated Azure Functions host instance
var host = new HostBuilder()
    // Configures defaults for worker execution and enables OpenAPI extension handling
    .ConfigureFunctionsWebApplication(builder =>
    {
        builder.UseNewtonsoftJson();
    })
    // Register custom services into the Dependency Injection (DI) container
    .ConfigureServices(services =>
    {
        // Register the worker telemetry SDK before enabling Functions Application Insights integration
        services.AddApplicationInsightsTelemetryWorkerService();
        // Adds telemetry processing features specifically tailored for isolated worker processes
        services.ConfigureFunctionsApplicationInsights();
    })
    // Constructs the configured host object
    .Build();

// Asynchronously start execution of the worker application to listen for incoming events/triggers
await host.RunAsync();