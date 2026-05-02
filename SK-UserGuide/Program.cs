using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using SK_UserGuide.Configuration;
using SK_UserGuide.Services.Abstract;
using SK_UserGuide.Services.Concrete;
using SK_UserGuide.Services.Ingestion;
using SK_UserGuide.Services.LLM;
using SK_UserGuide.Services.Retrieval;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// CORS policy for API access from CP and other local projects
var allowedOrigins = builder.Configuration["Cors:AllowedOrigins"] ?? "";
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowCP", policy =>
    {
        policy.SetIsOriginAllowed(origin =>
              {
                  var host = new Uri(origin).Host;
                  if (host == "localhost" || host == "127.0.0.1")
                      return true;

                  // Support additional origins from config (comma-separated)
                  return allowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries)
                      .Any(o => origin.StartsWith(o.Trim(), StringComparison.OrdinalIgnoreCase));
              })
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Add services to the container
builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// ──────────────────────────────────────────────
// 1. Configuration binding (model-agnostic)
// ──────────────────────────────────────────────
builder.Services.Configure<LlmSettings>(builder.Configuration.GetSection("Llm"));
builder.Services.Configure<OpenAiSettings>(builder.Configuration.GetSection("OpenAI"));
builder.Services.Configure<AzureOpenAiSettings>(builder.Configuration.GetSection("AzureOpenAI"));
builder.Services.Configure<QdrantSettings>(builder.Configuration.GetSection("Qdrant"));
builder.Services.Configure<RagSettings>(builder.Configuration.GetSection("Rag"));

var llmSettings = builder.Configuration.GetSection("Llm").Get<LlmSettings>() ?? new LlmSettings();
var openAiSettings = builder.Configuration.GetSection("OpenAI").Get<OpenAiSettings>() ?? new OpenAiSettings();
var azureSettings = builder.Configuration.GetSection("AzureOpenAI").Get<AzureOpenAiSettings>() ?? new AzureOpenAiSettings();

// ──────────────────────────────────────────────
// 2. Qdrant HTTP Client
// ──────────────────────────────────────────────
builder.Services.AddHttpClient<QdrantRestClient>();

var kernelBuilder = Kernel.CreateBuilder();

switch (llmSettings.Provider)
{
    case "OpenAI":
        ValidateOpenAiSettings(openAiSettings);

        kernelBuilder.AddOpenAIChatCompletion(
            modelId: openAiSettings.ChatModel,
            apiKey: openAiSettings.ApiKey);
        break;

    case "AzureOpenAI":
        ValidateAzureSettings(azureSettings);

        kernelBuilder.AddAzureOpenAIChatCompletion(
            deploymentName: azureSettings.ChatDeploymentName,
            endpoint: azureSettings.Endpoint,
            apiKey: azureSettings.ApiKey);
        break;

    // Future providers can be added here:
    // case "Claude":
    //     kernelBuilder.AddChatCompletion(...);
    //     break;

    default:
        throw new InvalidOperationException(
            $"Unsupported LLM provider: '{llmSettings.Provider}'. " +
            $"Supported providers: OpenAI, AzureOpenAI");
}

builder.Services.AddSingleton(kernelBuilder.Build());

// ──────────────────────────────────────────────
// 4. Embedding Generator - Model-Agnostic
// ──────────────────────────────────────────────
switch (llmSettings.Provider)
{
    case "OpenAI":
        builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
        {
            var tempBuilder = Microsoft.SemanticKernel.Kernel.CreateBuilder();

            tempBuilder.AddOpenAIEmbeddingGenerator(
                modelId: openAiSettings.EmbeddingModel,
                apiKey: openAiSettings.ApiKey);

            return tempBuilder.Build().GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
        });
        break;

    case "AzureOpenAI":
#pragma warning disable SKEXP0010
        builder.Services.AddAzureOpenAIEmbeddingGenerator(
            deploymentName: azureSettings.EmbeddingDeploymentName,
            endpoint: azureSettings.Endpoint,
            apiKey: azureSettings.ApiKey);
#pragma warning restore SKEXP0010
        break;

    default:
        throw new InvalidOperationException(
            $"No embedding generator configured for provider: '{llmSettings.Provider}'");
}

// ──────────────────────────────────────────────
// 5. Hybrid Retrieval Service
// ──────────────────────────────────────────────
builder.Services.AddHttpClient<IHybridRetrievalService, HybridRetrievalService>();

// 6. RAG Services
builder.Services.AddScoped<PromptBuilder>();
builder.Services.AddScoped<IRagRetrievalService, RagRetrievalService>();
builder.Services.AddScoped<IRagService, RagService>();

// ──────────────────────────────────────────────
// 7. Ingestion Pipeline Services
// ──────────────────────────────────────────────
builder.Services.AddScoped<TextCleaner>();
builder.Services.AddScoped<PdfTextExtractor>();
builder.Services.AddScoped<TxtTextExtractor>();
builder.Services.AddScoped<DocxTextExtractor>();
builder.Services.AddScoped<StructuralChunker>();
builder.Services.AddScoped<DocumentMetadataBuilder>();
builder.Services.AddHttpClient<IQdrantIngestionRepository, QdrantIngestionRepository>();
builder.Services.AddScoped<IDocumentIngestionOrchestrator, DocumentIngestionOrchestrator>();


var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseCors("AllowCP");
app.UseAuthorization();

// Enable Swagger (even in Production for easier testing of this API)
app.UseSwagger();
app.UseSwaggerUI();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

// ──────────────────────────────────────────────
// Helper Methods
// ──────────────────────────────────────────────
static void ValidateOpenAiSettings(OpenAiSettings settings)
{
    var errors = new List<string>();

    if (string.IsNullOrWhiteSpace(settings.ApiKey))
        errors.Add("OpenAI:ApiKey is not configured");

    if (errors.Count > 0)
    {
        throw new InvalidOperationException(
            "OpenAI configuration is incomplete. " +
            "Please set the following via environment variables or user-secrets:\n" +
            string.Join("\n", errors.Select(e => $"  - {e}")));
    }
}

static void ValidateAzureSettings(AzureOpenAiSettings settings)
{
    var errors = new List<string>();

    if (string.IsNullOrWhiteSpace(settings.Endpoint))
        errors.Add("AzureOpenAI:Endpoint is not configured");
    if (string.IsNullOrWhiteSpace(settings.ApiKey))
        errors.Add("AzureOpenAI:ApiKey is not configured");
    if (string.IsNullOrWhiteSpace(settings.ChatDeploymentName))
        errors.Add("AzureOpenAI:ChatDeploymentName is not configured");
    if (string.IsNullOrWhiteSpace(settings.EmbeddingDeploymentName))
        errors.Add("AzureOpenAI:EmbeddingDeploymentName is not configured");

    if (errors.Count > 0)
    {
        throw new InvalidOperationException(
            "Azure OpenAI configuration is incomplete. " +
            "Please set the following via environment variables or user-secrets:\n" +
            string.Join("\n", errors.Select(e => $"  - {e}")));
    }
}
