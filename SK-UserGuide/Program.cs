using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using SK_UserGuide.Configuration;
using SK_UserGuide.Services.Abstract;
using SK_UserGuide.Services.Concrete;
using SK_UserGuide.Services.Ingestion;
using SK_UserGuide.Services.Retrieval;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    });

// Encoding provider for Turkish characters
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// 1. Configuration binding
builder.Services.Configure<OllamaSettings>(builder.Configuration.GetSection("Ollama"));
builder.Services.Configure<QdrantSettings>(builder.Configuration.GetSection("Qdrant"));

// 2. Qdrant REST Client (HTTP/1.1 - port 6333, bypasses HTTP/2 proxy issues)
builder.Services.AddHttpClient<QdrantRestClient>();

// 3. Embedding Generator
builder.Services.AddHttpClient<OllamaEmbeddingService>();
builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>, OllamaEmbeddingService>();

// 4. Semantic Kernel with custom HttpClient (long timeout for Ollama)
var ollamaSettings = builder.Configuration.GetSection("Ollama").Get<OllamaSettings>() ?? new OllamaSettings();

// Register HttpClient for Semantic Kernel with extended timeout
builder.Services.AddHttpClient("SemanticKernelClient", client =>
{
    client.Timeout = TimeSpan.FromMinutes(5); // 5 minute timeout for slow LLM responses
});

// Build kernel with Ollama
var kernelBuilder = Kernel.CreateBuilder();

#pragma warning disable SKEXP0010
kernelBuilder.AddOpenAIChatCompletion(
    modelId: ollamaSettings.ChatModel,
    endpoint: new Uri($"{ollamaSettings.BaseUrl}/v1"),
    apiKey: "ignore");
#pragma warning restore SKEXP0010

builder.Services.AddSingleton(kernelBuilder.Build());

// 5. Hybrid Retrieval Service (NEW)
builder.Services.AddHttpClient<HybridRetrievalService>();

// 6. Chat History Manager (in-memory, singleton)
builder.Services.AddSingleton<SK_UserGuide.Services.Chat.ChatHistoryManager>();
builder.Services.AddSingleton<SK_UserGuide.Services.Chat.ResponseCache>();

// 7. Session support for chat history
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// 8. RAG Services
builder.Services.AddScoped<RagIngestionService>();
builder.Services.AddScoped<RagRetrievalService>();
builder.Services.AddScoped<IRagService, RagService>();

// 7. Ingestion Pipeline Services
builder.Services.AddScoped<TextCleaner>();
builder.Services.AddScoped<PdfTextExtractor>();
builder.Services.AddScoped<TxtTextExtractor>();
builder.Services.AddScoped<StructuralChunker>();
builder.Services.AddScoped<DocumentMetadataBuilder>();
builder.Services.AddHttpClient<VersionManager>();
builder.Services.AddHttpClient<QdrantIngestionRepository>();
builder.Services.AddScoped<DocumentIngestionOrchestrator>();


var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseSession();  // Enable session for chat history
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
