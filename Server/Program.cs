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
using NCATAIBlazorFrontendTest.Server.Recursor.Seeding;
using NCATAIBlazorFrontendTest.Server.Recursor.ML;
using Microsoft.AspNetCore.Hosting;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;
using NCATAIBlazorFrontendTest.Server.Recursor.Services.SimEventInterpretation;
using Kusto.Data.Net.Client;
using Microsoft.Extensions.Logging;



var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.AddApplicationInsightsTelemetry();
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

// In-memory repositories (singleton — session state, sim catalog, and user thresholds stay in memory).
builder.Services.AddSingleton<ISessionRepository, SessionRepository>();
builder.Services.AddSingleton<ISimCatalogRepository, SimCatalogRepository>();
builder.Services.AddSingleton<IUserThresholdRepository, InMemoryUserThresholdRepository>();
// Phase 6D: InMemoryUserProfileRepository is registered as itself so HybridUserProfileRepository
// can inject it as the concrete fallback store.  IUserProfileRepository resolves to the hybrid
// implementation, which queries ADX first and falls back to in-memory on miss or failure.
builder.Services.AddSingleton<InMemoryUserProfileRepository>();
builder.Services.AddSingleton<IUserProfileRepository, HybridUserProfileRepository>();

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
builder.Services.AddSingleton<IAdxModelEvaluationQueryService, AdxModelEvaluationQueryService>();
builder.Services.AddSingleton<IAdxUserBaselineQueryService, AdxUserBaselineQueryService>();
builder.Services.AddSingleton<IAdxUserProfileQueryService, AdxUserProfileQueryService>();
builder.Services.AddSingleton<IAdxTrainingExportService, AdxTrainingExportService>();
// Phase 9A: offline policy recommendation engine (analytics-only, never called on hot path).
builder.Services.AddSingleton<IAdxPolicyRecommendationQueryService, AdxPolicyRecommendationQueryService>();
builder.Services.AddSingleton<IPolicyRecommendationService, PolicyRecommendationService>();
builder.Services.AddSingleton<IUserRelativeSignalService, UserRelativeSignalService>();
builder.Services.AddSingleton<IUserRelativePolicyAdvisorService, UserRelativePolicyAdvisorService>();
builder.Services.AddSingleton<IUserThresholdDerivationService, UserThresholdDerivationService>();
builder.Services.AddSingleton<IUserProfileUpdateService, UserProfileUpdateService>();

// Phase 10S-2: sim-specific event interpretation adapters (singleton — stateless).
builder.Services.AddSingleton<DefaultSimEventInterpretationAdapter>();
builder.Services.AddSingleton<MedicalSupplyEventInterpretationAdapter>();
builder.Services.AddSingleton<ISimEventInterpretationAdapterFactory, SimEventInterpretationAdapterFactory>();

// Recursor pipeline services (scoped — one per request).
builder.Services.AddScoped<IFeatureExtractionService, FeatureExtractionService>();
builder.Services.AddScoped<IBehaviorInterpreter, BehaviorInterpreter>();
builder.Services.AddScoped<IAdaptationPolicyService, AdaptationPolicyService>();
builder.Services.AddScoped<IRecursorIngestionService, RecursorIngestionService>();
builder.Services.AddScoped<ITrajectoryAnalysisService, TrajectoryAnalysisService>();
builder.Services.AddScoped<IRecursorSessionService, RecursorSessionService>();
builder.Services.AddScoped<IBehaviorScoringService, BehaviorScoringService>();
builder.Services.AddScoped<IExplanationGenerationService, AzureOpenAiExplanationService>();
builder.Services.AddScoped<IBehaviorStateFeatureVectorBuilder, BehaviorStateFeatureVectorBuilder>();
builder.Services.AddScoped<IMultiSignalGuardrailService, MultiSignalGuardrailService>();
builder.Services.AddScoped<IPhase8AGuardrailModifierService, Phase8AGuardrailModifierService>();
// Phase 8B: singleton — holds pending adaptation evaluations across requests.
builder.Services.AddSingleton<IAdaptationEffectivenessService, AdaptationEffectivenessService>();
// Recursor ML prediction: use real ML.NET service if at least one model file is configured
// and present; otherwise fall back to the no-op shadow service so the app starts without
// any trained models.
static string? ResolveModelPath(string? configured, string contentRoot) =>
    string.IsNullOrWhiteSpace(configured)
        ? null
        : Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(contentRoot, configured);

var resolvedHintDependencePath = ResolveModelPath(
    builder.Configuration["Recursor:Models:HintDependenceModelPath"],
    builder.Environment.ContentRootPath);

var resolvedConfusionPath = ResolveModelPath(
    builder.Configuration["Recursor:Models:ConfusionModelPath"],
    builder.Environment.ContentRootPath);

var resolvedStableMasteryPath = ResolveModelPath(
    builder.Configuration["Recursor:Models:StableMasteryModelPath"],
    builder.Environment.ContentRootPath);

var resolvedHintDependenceNextWindowPath = ResolveModelPath(
    builder.Configuration["Recursor:Models:HintDependenceNextWindowModelPath"],
    builder.Environment.ContentRootPath);



// Bind policy options so AdaptationPolicyService can read guardrail thresholds.
builder.Services.Configure<RecursorPoliciesOptions>(
    builder.Configuration.GetSection("Recursor:Policies"));
// Phase 8E: reliability weighting options and service.
builder.Services.Configure<RecursorPolicyReliabilityOptions>(
    builder.Configuration.GetSection("Recursor:PolicyReliability"));
builder.Services.AddScoped<IPolicyReliabilityWeightingService, PolicyReliabilityWeightingService>();
// Phase 10A: sequence-aware feature extraction and trajectory classification.
builder.Services.AddScoped<ISequenceFeatureExtractor, SequenceFeatureExtractor>();
// Phase 10S-1: sim adapter contract validator — diagnostic only, no pipeline side effects.
builder.Services.AddSingleton<IRecursorSimContractValidator, RecursorSimContractValidator>();
// Phase 10B: temporal embedding and prediction target generation.
builder.Services.AddScoped<ITemporalEmbeddingService, TemporalEmbeddingService>();

// Phase 10C-2: temporal risk predictor — ADX query service, training service, and prediction service.
builder.Services.AddSingleton<IAdxTemporalTrainingQueryService, AdxTemporalTrainingQueryService>();
// Phase 14A: internal evaluation dashboard — read-only ADX query service.
builder.Services.AddSingleton<IAdxDashboardQueryService, AdxDashboardQueryService>();
// Phase 16A: Sim Explorer — read-only browse by sim/user/session.
builder.Services.AddSingleton<IAdxSimExplorerQueryService, AdxSimExplorerQueryService>();
builder.Services.AddSingleton<ITemporalRiskModelTrainingService, TemporalRiskModelTrainingService>();

var resolvedTemporalRiskH1Path = ResolveModelPath(
    builder.Configuration["Recursor:Models:TemporalRiskH1ModelPath"],
    builder.Environment.ContentRootPath);
var resolvedTemporalRiskH2Path = ResolveModelPath(
    builder.Configuration["Recursor:Models:TemporalRiskH2ModelPath"],
    builder.Environment.ContentRootPath);
var resolvedTemporalRiskH3Path = ResolveModelPath(
    builder.Configuration["Recursor:Models:TemporalRiskH3ModelPath"],
    builder.Environment.ContentRootPath);

builder.Services.AddSingleton<ITemporalRiskPredictionService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<TemporalRiskPredictionService>>();
    return new TemporalRiskPredictionService(
        logger,
        resolvedTemporalRiskH1Path,
        resolvedTemporalRiskH2Path,
        resolvedTemporalRiskH3Path);
});

// Phase 10D-1/10D-4: binary elevated-risk predictor — training service and prediction service.
builder.Services.AddSingleton<ITemporalElevatedRiskModelTrainingService, TemporalElevatedRiskModelTrainingService>();

var elevatedRiskModelVersion =
    builder.Configuration["Recursor:Models:TemporalElevatedRiskModelVersion"]
    ?? TemporalElevatedRiskPredictionService.ModelVersion;

// Use the same version-based path resolver as the trainer — setting TemporalElevatedRiskModelVersion
// is sufficient to load the right model files without also updating each HnModelPath key.
var resolvedElevatedRiskH1Path = TemporalElevatedRiskModelTrainingService.ResolveVersionedModelPath(
    1, elevatedRiskModelVersion, builder.Environment.ContentRootPath,
    builder.Configuration["Recursor:Models:TemporalElevatedRiskH1ModelPath"]);
var resolvedElevatedRiskH2Path = TemporalElevatedRiskModelTrainingService.ResolveVersionedModelPath(
    2, elevatedRiskModelVersion, builder.Environment.ContentRootPath,
    builder.Configuration["Recursor:Models:TemporalElevatedRiskH2ModelPath"]);
var resolvedElevatedRiskH3Path = TemporalElevatedRiskModelTrainingService.ResolveVersionedModelPath(
    3, elevatedRiskModelVersion, builder.Environment.ContentRootPath,
    builder.Configuration["Recursor:Models:TemporalElevatedRiskH3ModelPath"]);

builder.Services.AddSingleton<ITemporalElevatedRiskPredictionService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<TemporalElevatedRiskPredictionService>>();
    return new TemporalElevatedRiskPredictionService(
        logger,
        resolvedElevatedRiskH1Path,
        resolvedElevatedRiskH2Path,
        resolvedElevatedRiskH3Path,
        elevatedRiskModelVersion);
});

bool anyModelPresent =
    (resolvedHintDependencePath            is not null && File.Exists(resolvedHintDependencePath))            ||
    (resolvedConfusionPath                 is not null && File.Exists(resolvedConfusionPath))                 ||
    (resolvedStableMasteryPath             is not null && File.Exists(resolvedStableMasteryPath))             ||
    (resolvedHintDependenceNextWindowPath  is not null && File.Exists(resolvedHintDependenceNextWindowPath));

if (anyModelPresent)
{
    builder.Services.AddSingleton<IBehaviorStatePredictionService>(sp =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var logger = sp.GetRequiredService<ILogger<MlNetBehaviorStatePredictionService>>();

        var hintDependencePath = config["Recursor:Models:HintDependenceModelPath"];
        var confusionPath = config["Recursor:Models:ConfusionModelPath"];
        var stableMasteryPath = config["Recursor:Models:StableMasteryModelPath"];
        var nextWindowPath = config["Recursor:Models:HintDependenceNextWindowModelPath"];

        return new MlNetBehaviorStatePredictionService(
            logger,
            hintDependencePath,
            confusionPath,
            stableMasteryPath,
            nextWindowPath,
            "mlnet-multi-v1"
            );
    });
}
else
{
    builder.Services.AddScoped<IBehaviorStatePredictionService, ShadowBehaviorStatePredictionService>();
}
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

// Phase 8D — seed test user thresholds for live validation.
// Seeds two contrasting users (recursor-test-sensitive / recursor-test-tolerant)
// so manual runs can exercise per-user threshold overrides without real ADX data.
RecursorTestThresholdSeeder.Seed(app.Services.GetRequiredService<IUserThresholdRepository>());

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
/*try
{
    NCATAIBlazorFrontendTest.Server.Recursor.ML.RecursorMlTrainingRunner
        .TrainHintDependenceNextWindow_WithEmbeddings();
}
catch (Exception ex)
{
    var failPath = Path.Combine(builder.Environment.ContentRootPath, "training_failure.log");
    File.WriteAllText(failPath, ex.ToString());
    throw;
}*/
//NCATAIBlazorFrontendTest.Server.Recursor.ML.RecursorMlTrainingRunner.TrainHintDependenceModel_WithEmbeddings();
app.Run();
