using Microsoft.AspNetCore.Mvc;
using SK_UserGuide.Services.Abstract;
using SK_UserGuide.Services.Concrete;

namespace SK_UserGuide.Controllers;

public class ChatController : Controller
{
    private readonly IRagService _ragService;
    private readonly QdrantRestClient _qdrantClient;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        IRagService ragService,
        QdrantRestClient qdrantClient,
        ILogger<ChatController> logger)
    {
        _ragService = ragService;
        _qdrantClient = qdrantClient;
        _logger = logger;
    }

    /// <summary>
    /// Server-Sent Events endpoint for streaming LLM responses.
    /// </summary>
    [HttpGet]
    public async Task AskStreaming(string question, string tenantId, CancellationToken cancellationToken = default)
    {
        // Inform browser we're sending chunked data
        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        if (string.IsNullOrWhiteSpace(question))
        {
            await Response.WriteAsync("data: Empty message cannot be sent.\n\n", cancellationToken);
            await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            await Response.WriteAsync("data: Please select a tenant before asking a question.\n\n", cancellationToken);
            await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
            return;
        }

        try
        {
            await foreach (var chunk in _ragService.AskStreamingAsync(question, tenantId.Trim(), cancellationToken))
            {
                var escapedChunk = chunk.Replace("\n", "\\n").Replace("\r", "");
                await Response.WriteAsync($"data: {escapedChunk}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);

            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected
        }
        catch (QdrantCollectionNotFoundException ex)
        {
            _logger.LogWarning(ex, "Tenant collection not found for chat request: {TenantId}", tenantId);
            await Response.WriteAsync("data: Tenant collection was not found. Create the tenant and upload documents before asking questions.\n\n", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chat streaming failed for tenant {TenantId}", tenantId);
            await Response.WriteAsync("data: An error occurred while processing your request.\n\n", cancellationToken);
        }

        await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? tenantId = null)
    {
        var tenants = new List<string>();
        string? selectedTenantId = null;

        try
        {
            tenants = await _qdrantClient.ListCollectionsAsync();
            if (!string.IsNullOrWhiteSpace(tenantId) && tenants.Contains(tenantId))
            {
                selectedTenantId = tenantId;
            }
            else
            {
                selectedTenantId = tenants.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to load tenant collections for chat UI.");
            ViewBag.TenantLoadError = "Tenant collections could not be loaded. Check the Qdrant connection.";
        }

        ViewBag.Tenants = tenants;
        ViewBag.SelectedTenantId = selectedTenantId ?? string.Empty;
        return View();
    }
}
