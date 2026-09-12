using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Build and initialize the isolated Azure Functions host instance
var host = new HostBuilder()
    // Configures defaults for worker execution (HTTP triggers, gRPC channels, metadata decoding)
    .ConfigureFunctionsWorkerDefaults()
    // Register custom services into the Dependency Injection (DI) container
    .ConfigureServices(services =>
    {
        // Adds telemetry processing features specifically tailored for isolated worker processes
        services.ConfigureFunctionsApplicationInsights();
    })
    // Constructs the configured host object
    .Build();

// Asynchronously start execution of the worker application to listen for incoming events/triggers
await host.RunAsync();