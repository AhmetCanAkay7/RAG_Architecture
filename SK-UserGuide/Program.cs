using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using SK_UserGuide.Configuration;
using SK_UserGuide.Services.Abstract;
using SK_UserGuide.Services.Concrete;
using SK_UserGuide.Services.Ingestion; // Document processing
using SK_UserGuide.Services.Retrieval; // chunk search
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// CORS policy for API access from other local projects
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowCP", policy =>
    {
        policy.SetIsOriginAllowed(origin =>
                new Uri(origin).Host == "localhost" ||
                new Uri(origin).Host == "127.0.0.1")
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

// Swagger/OpenAPI support
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Encoding provider for Turkish characters
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// 1. Configuration binding
builder.Services.Configure<OllamaSettings>(builder.Configuration.GetSection("Ollama"));
builder.Services.Configure<QdrantSettings>(builder.Configuration.GetSection("Qdrant"));
builder.Services.Configure<RagSettings>(builder.Configuration.GetSection("Rag"));

builder.Services.AddHttpClient<QdrantRestClient>();

// 3. Embedding Generator
builder.Services.AddHttpClient<OllamaEmbeddingService>();
builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>, OllamaEmbeddingService>();

var ollamaSettings = builder.Configuration.GetSection("Ollama").Get<OllamaSettings>() ?? new OllamaSettings();



// Build kernel with Ollama
var kernelBuilder = Kernel.CreateBuilder();

#pragma warning disable SKEXP0010
kernelBuilder.AddOpenAIChatCompletion(
    modelId: ollamaSettings.ChatModel,
    endpoint: new Uri($"{ollamaSettings.BaseUrl}/v1"),
    apiKey: "ignore");
#pragma warning restore SKEXP0010

builder.Services.AddSingleton(kernelBuilder.Build());

// 5. Hybrid Retrieval Service
builder.Services.AddHttpClient<IHybridRetrievalService, HybridRetrievalService>();

// 6. Response Cache (in-memory, singleton)
builder.Services.AddSingleton<SK_UserGuide.Services.Chat.ResponseCache>();

// 8. RAG Services
builder.Services.AddScoped<IRagRetrievalService, RagRetrievalService>();
builder.Services.AddScoped<IRagService, RagService>();

// 7. Ingestion Pipeline Services
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
