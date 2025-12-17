using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Connectors.Qdrant;
using Microsoft.SemanticKernel.Embeddings;
using Qdrant.Client;
using SK_UserGuide.Services.Abstract;
using SK_UserGuide.Services.Concrete;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    });

// Encoding provider for Turkish characters
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// 1. OLLAMA AYARLARI
string ollamaUrl = "http://localhost:11434/v1";
string chatModel = "phi3:3.8b";

// 2. Qdrant Client
builder.Services.AddSingleton<QdrantClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var hostConfig = config["Qdrant:Host"];
    if (string.IsNullOrEmpty(hostConfig)) throw new InvalidOperationException("Qdrant Host not configured");
    Uri uri = new Uri(hostConfig);
    string hostname = uri.Host;
    int port = int.Parse(config["Qdrant:Port"] ?? "6334"); // Use config port, default 6334 for Qdrant Cloud
    bool https = uri.Scheme == "https";
    var apiKey = config["Qdrant:ApiKey"];
    return new QdrantClient(new Uri($"https://{hostname}:{port}"));
});

// 3. Embedding Service
builder.Services.AddHttpClient<OllamaEmbeddingService>();
builder.Services.AddSingleton<ITextEmbeddingGenerationService, OllamaEmbeddingService>();

// 4. Vector Store
builder.Services.AddSingleton<QdrantVectorStore>(sp =>
{
    var client = sp.GetRequiredService<QdrantClient>();
    return new QdrantVectorStore(client, false);
});

// 5. KERNEL BUILDER
var kernelBuilder = Kernel.CreateBuilder();
#pragma warning disable SKEXP0010
kernelBuilder.AddOpenAIChatCompletion(chatModel, new Uri(ollamaUrl), "ignore");
#pragma warning restore SKEXP0010
builder.Services.AddSingleton(kernelBuilder.Build());

// 6. RAG Services
builder.Services.AddScoped<RagIngestionService>();
builder.Services.AddScoped<RagRetrievalService>();
builder.Services.AddScoped<IRagService, RagService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
