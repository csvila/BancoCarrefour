using BancoCarrefour.ServiceDefaults;

// Host do deployable Core Banking (ADR-0009): compõe os módulos Accounts e Ledger.
// Contratos internos em gRPC versionado; nenhum serviço de domínio nesta etapa (skeleton).
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = false;
    options.MaxReceiveMessageSize = 1 * 1024 * 1024;
});
builder.Services.AddGrpcHealthChecks();

var app = builder.Build();

app.UseServiceDefaults();

// grpc.health.v1 para probes gRPC; anônimo como os endpoints HTTP de health.
app.MapGrpcHealthChecksService().AllowAnonymous();
app.MapDefaultEndpoints();

await app.RunAsync();
