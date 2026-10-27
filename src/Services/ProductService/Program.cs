using BuildingBlocks.Contracts;
using BuildingBlocks.Health;
using BuildingBlocks.Messaging;
using BuildingBlocks.Observability;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using ProductService.Chaos;
using ProductService.Data;
using ProductService.Endpoints;
using ProductService.Messaging;

var builder = WebApplication.CreateBuilder(args);

// ---- Logging (Serilog -> console + Seq) ----
builder.AddServiceLogging("ProductService");

// ---- Database (own database: productdb) ----
builder.Services.AddDbContext<ProductDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("ProductDb")));

// ---- RabbitMQ: subscribe to the commands this service handles ----
builder.Services.AddRabbitMqMessaging(builder.Configuration)
    .AddHandler<ReserveStockCommand, ReserveStockHandler>()
    .AddHandler<ReleaseStockCommand, ReleaseStockHandler>();

// ---- Health checks (/health/live, /health/ready) ----
builder.Services.AddServiceHealthChecks().AddDatabaseHealthCheck<ProductDbContext>();

builder.Services.AddSingleton<ChaosSettings>();
builder.Services.AddProblemDetails();
builder.Services.AddServiceOpenApi("Product Service");

var app = builder.Build();

app.UseExceptionHandler();
app.UseCorrelationId();
app.UseServiceRequestLogging();

app.MapServiceOpenApi("Product Service");   // /openapi/v1.json + /swagger
app.MapServiceHealthChecks();
app.MapProductEndpoints();

await app.InitializeDatabaseAsync<ProductDbContext>(ProductSeeder.SeedAsync);

app.Run();
