using System.Text.Json.Serialization;
using BuildingBlocks.Contracts;
using BuildingBlocks.Health;
using BuildingBlocks.Messaging;
using BuildingBlocks.Observability;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using OrderService.Clients;
using OrderService.Data;
using OrderService.Endpoints;
using OrderService.Saga;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceLogging("OrderService");

builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("OrderDb")));

// ---- Synchronous HTTP to ProductService, protected by retry + CIRCUIT BREAKER + timeout ----
builder.Services.AddProductCatalogClient(builder.Configuration);

// ---- SAGA: the orchestrator is used by the API (StartAsync) and handles all reply events ----
builder.Services.AddScoped<OrderSagaOrchestrator>();
builder.Services.AddRabbitMqMessaging(builder.Configuration)
    .AddHandler<StockReservedEvent, OrderSagaOrchestrator>()
    .AddHandler<StockReservationFailedEvent, OrderSagaOrchestrator>()
    .AddHandler<PaymentSucceededEvent, OrderSagaOrchestrator>()
    .AddHandler<PaymentFailedEvent, OrderSagaOrchestrator>()
    .AddHandler<StockReleasedEvent, OrderSagaOrchestrator>();

builder.Services.AddServiceHealthChecks().AddDatabaseHealthCheck<OrderDbContext>();

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddServiceOpenApi("Order Service");

var app = builder.Build();

app.UseExceptionHandler();
app.UseCorrelationId();
app.UseServiceRequestLogging();

app.MapServiceOpenApi("Order Service");   // /openapi/v1.json + /swagger
app.MapServiceHealthChecks();
app.MapOrderEndpoints();

await app.InitializeDatabaseAsync<OrderDbContext>();

app.Run();
