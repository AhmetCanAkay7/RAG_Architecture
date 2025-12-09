using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Memory;
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
string embedModel = "nomic-embed-text";

// 2. KERNEL BUILDER
var kernelBuilder = Kernel.CreateBuilder();
#pragma warning disable SKEXP0010
kernelBuilder.AddOpenAIChatCompletion(chatModel, new Uri(ollamaUrl), "ignore");
#pragma warning restore SKEXP0010
builder.Services.AddSingleton(kernelBuilder.Build());

// 3. MEMORY (RAM KULLANIYORUZ)
#pragma warning disable SKEXP0001, SKEXP0010, SKEXP0050
var httpClient = new HttpClient { BaseAddress = new Uri(ollamaUrl) };
var embeddingService = new OpenAITextEmbeddingGenerationService(embedModel, "ignore", httpClient: httpClient);

var memoryBuilder = new MemoryBuilder()
    .WithMemoryStore(new VolatileMemoryStore())
    .WithTextEmbeddingGeneration(embeddingService);
    
builder.Services.AddSingleton<ISemanticTextMemory>(memoryBuilder.Build());
#pragma warning restore SKEXP0001, SKEXP0010, SKEXP0050

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
