using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Qdrant.Client;
using SK_UserGuide.Configuration;
using SK_UserGuide.Services.Abstract;
using SK_UserGuide.Services.Concrete;
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

// 2. Qdrant Client (gRPC - port 6334)
builder.Services.AddSingleton<QdrantClient>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<QdrantSettings>>().Value;
    return new QdrantClient("localhost", 6334, false);
});

// 3. Embedding Generator
builder.Services.AddHttpClient<OllamaEmbeddingService>();
builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>, OllamaEmbeddingService>();

// 4. Semantic Kernel
var ollamaSettings = builder.Configuration.GetSection("Ollama").Get<OllamaSettings>() ?? new OllamaSettings();
var kernelBuilder = Kernel.CreateBuilder();

#pragma warning disable SKEXP0010
kernelBuilder.AddOpenAIChatCompletion(
    ollamaSettings.ChatModel,
    new Uri($"{ollamaSettings.BaseUrl}/v1"),
    "ignore");
#pragma warning restore SKEXP0010

builder.Services.AddSingleton(kernelBuilder.Build());

// 5. RAG Services
builder.Services.AddScoped<RagIngestionService>();
builder.Services.AddScoped<RagRetrievalService>();
builder.Services.AddScoped<IRagService, RagService>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
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
