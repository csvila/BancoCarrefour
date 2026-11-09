using Asp.Versioning;
using BancoCarrefour.ServiceDefaults;

// Payments Service (ADR-0009): API REST externa versionada em /v1 com OpenAPI e Problem Details.
// Skeleton: sem endpoints de negócio nesta etapa.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services
    .AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.AssumeDefaultVersionWhenUnspecified = false;
        options.ReportApiVersions = true;
        options.ApiVersionReader = new UrlSegmentApiVersionReader();
    })
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    })
    // Um documento OpenAPI por versão de API (integração oficial Asp.Versioning + Microsoft.AspNetCore.OpenApi).
    .AddOpenApi();

var app = builder.Build();

app.UseServiceDefaults();

var versionSet = app.NewApiVersionSet()
    .HasApiVersion(new ApiVersion(1, 0))
    .ReportApiVersions()
    .Build();

var v1 = app.MapGroup("/v{version:apiVersion}")
    .WithApiVersionSet(versionSet)
    .HasApiVersion(new ApiVersion(1, 0));

// Endpoint técnico sem dados: prova o pipeline Bearer (fallback policy) + versionamento para a suíte negativa
// da ADR-0006. Não retorna claims nem informação do chamador.
v1.MapGet("/ping", static () => TypedResults.NoContent())
    .WithName("PingV1")
    .WithSummary("Verifica autenticação Bearer e roteamento da versão 1.");

app.MapDefaultEndpoints();

// Documento OpenAPI exposto anonimamente apenas em Development; publicação em outros ambientes via gateway (a decidir).
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().WithDocumentPerVersion().AllowAnonymous();
}

await app.RunAsync();
