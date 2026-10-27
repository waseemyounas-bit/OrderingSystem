using System.Text.Json.Serialization;
using BuildingBlocks.Contracts;
using BuildingBlocks.Health;
using BuildingBlocks.Messaging;
using BuildingBlocks.Observability;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using PaymentService.Data;
using PaymentService.Domain;
using PaymentService.Endpoints;
using PaymentService.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceLogging("PaymentService");

builder.Services.AddDbContext<PaymentDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PaymentDb")));

builder.Services.Configure<PaymentOptions>(builder.Configuration.GetSection("Payment"));

// PaymentService has no HTTP "create payment" endpoint: payments are only triggered by the saga via RabbitMQ.
builder.Services.AddRabbitMqMessaging(builder.Configuration)
    .AddHandler<ProcessPaymentCommand, ProcessPaymentHandler>();

builder.Services.AddServiceHealthChecks().AddDatabaseHealthCheck<PaymentDbContext>();

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddServiceOpenApi("Payment Service");

var app = builder.Build();

app.UseExceptionHandler();
app.UseCorrelationId();
app.UseServiceRequestLogging();

app.MapServiceOpenApi("Payment Service");   // /openapi/v1.json + /swagger
app.MapServiceHealthChecks();
app.MapPaymentEndpoints();

await app.InitializeDatabaseAsync<PaymentDbContext>();

app.Run();
