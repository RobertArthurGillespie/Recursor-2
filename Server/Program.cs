using Azure.Search.Documents;
using Azure;
using Microsoft.AspNetCore.ResponseCompression;
using OpenAI.Embeddings;
using Azure.AI.OpenAI;
//using Swashbuckle.AspNetCore.SwaggerGen;
using Azure.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NCATAIBlazorFrontendTest.Server.Data;
using FFMpegCore;
using Kusto.Data;
using Kusto.Data.Common;
using Kusto.Ingest;
using NCATAIBlazorFrontendTest.Server.Configuration;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Server.Recursor.Repositories;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;
using Kusto.Data.Net.Client;



var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]))
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddApiAuthorization();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        builder => builder.AllowAnyOrigin() // Or a specific origin like "http://localhost:5001"
            .AllowAnyMethod()
            .AllowAnyHeader());
});
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

// Configure FFMpegCore to find the executables in the local app directory
GlobalFFOptions.Configure(options => options.BinaryFolder = Path.Combine(AppContext.BaseDirectory, "ffmpeg"));

// ── Recursor Engine ───────────────────────────────────────────────────────────

// In-memory repositories (singleton — session state and sim catalog stay in memory).
builder.Services.AddSingleton<ISessionRepository, SessionRepository>();
builder.Services.AddSingleton<ISimCatalogRepository, SimCatalogRepository>();

// Bind typed ADX options from the "Adx" config section.
builder.Services.Configure<AdxOptions>(builder.Configuration.GetSection("Adx"));
var adxOpts = builder.Configuration.GetSection("Adx").Get<AdxOptions>() ?? new AdxOptions();

// ADX Kusto clients — registered as singletons only when ClusterUri is configured.
// Auth mode is driven by AdxOptions.AuthMode:
//   "UserPrompt"       — interactive browser login (default; for local dev)
//   "ManagedIdentity"  — system-assigned MSI (for production Azure hosting)
//   "ServicePrincipal" — client ID + secret (for CI/CD or when MSI is unavailable)
if (!string.IsNullOrEmpty(adxOpts.ClusterUri))
{
    builder.Services.AddSingleton<ICslQueryProvider>(_ =>
        KustoClientFactory.CreateCslQueryProvider(BuildAdxCsb(adxOpts.ClusterUri, adxOpts)));

    builder.Services.AddSingleton<IKustoQueuedIngestClient>(_ =>
        KustoIngestFactory.CreateQueuedIngestClient(BuildAdxCsb(adxOpts.IngestUri, adxOpts)));
}
// else: ClusterUri is empty — clients are not registered.
// AdxIngestionService and AdxRecursorQueryService resolve via IServiceProvider.GetService<T>(),
// which returns null, and they skip ADX calls with a warning log.

// ADX services.
builder.Services.AddSingleton<IAdxIngestionService, AdxIngestionService>();
builder.Services.AddSingleton<IAdxRecursorQueryService, AdxRecursorQueryService>();

// Recursor pipeline services (scoped — one per request).
builder.Services.AddScoped<IFeatureExtractionService, FeatureExtractionService>();
builder.Services.AddScoped<IBehaviorInterpreter, BehaviorInterpreter>();
builder.Services.AddScoped<IAdaptationPolicyService, AdaptationPolicyService>();
builder.Services.AddScoped<IRecursorIngestionService, RecursorIngestionService>();
builder.Services.AddScoped<IRecursorSessionService, RecursorSessionService>();
builder.Services.AddScoped<IBehaviorScoringService, BehaviorScoringService>();
builder.Services.AddScoped<IExplanationGenerationService, AzureOpenAiExplanationService>();

// Builds a Kusto connection string for the given URI using the configured auth mode.
static KustoConnectionStringBuilder BuildAdxCsb(string uri, AdxOptions opts) =>
    opts.AuthMode switch
    {
        "ManagedIdentity" => new KustoConnectionStringBuilder(uri)
            .WithAadSystemManagedIdentity(),

        "ServicePrincipal" => new KustoConnectionStringBuilder(uri)
            .WithAadApplicationKeyAuthentication(
                opts.ClientId,
                opts.ClientSecret,
                opts.TenantId),

        _ => new KustoConnectionStringBuilder(uri)
            .WithAadUserPromptAuthentication(opts.TenantId),
    };

// ─────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Use the CORS policy
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();
app.UseHttpsRedirection();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseRouting();
// Add services to the container.

//builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen();

// Configure Azure OpenAI EmbeddingsClient
/*builder.Services.AddSingleton<EmbeddingsClient>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var endpoint = configuration["OpenAIEndpoint"];
    var key = configuration["OpenAIKey"];
    var deploymentName = configuration["OpenAIEmbeddingDeploymentName"];

    if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(deploymentName))
    {
        throw new InvalidOperationException("OpenAIEndpoint, OpenAIKey, or OpenAIEmbeddingDeploymentName is not configured.");
    }

    return new EmbeddingsClient(new Uri(endpoint), new AzureKeyCredential(key), deploymentName);
});

// Configure Azure OpenAI ChatCompletionsClient
builder.Services.AddSingleton<ChatCompletionsClient>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var endpoint = configuration["OpenAIEndpoint"];
    var key = configuration["OpenAIKey"];
    var chatDeploymentName = configuration["OpenAIChatDeploymentName"];

    if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(chatDeploymentName))
    {
        throw new InvalidOperationException("OpenAIEndpoint, OpenAIKey, or OpenAIChatDeploymentName is not configured.");
    }

    return new ChatCompletionsClient(new Uri(endpoint), new AzureKeyCredential(key), chatDeploymentName);
});*/

// Configure Azure AI Search Client
/*builder.Services.AddSingleton<SearchClient>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var endpoint = configuration["AzureAISearchEndpoint"];
    var key = configuration["AzureAISearchKey"];
    var indexName = configuration["AzureAISearchIndexName"];

    if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(indexName))
    {
        throw new InvalidOperationException("AzureAISearchEndpoint, AzureAISearchKey, or AzureAISearchIndexName is not configured.");
    }

    return new SearchClient(new Uri(endpoint), indexName, new AzureKeyCredential(key));
});*/


app.MapRazorPages();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
