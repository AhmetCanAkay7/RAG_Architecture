using Microsoft.AspNetCore.Mvc;
using SK_UserGuide.Services.Abstract;

namespace SK_UserGuide.Controllers;

/// <summary>
/// Chat controller with streaming SSE endpoint.
/// </summary>
public class ChatController : Controller
{
    private readonly IRagService _ragService;

    public ChatController(IRagService ragService)
    {
        _ragService = ragService;
    }

    /// <summary>
    /// Server-Sent Events endpoint for streaming LLM responses.
    /// </summary>
    [HttpGet]
    public async Task AskStreaming(string question, CancellationToken cancellationToken)
    {
        // browser'a parca parca veri gondereceğimizin haberini veriyoruz.
        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        if (string.IsNullOrWhiteSpace(question))
        {
            await Response.WriteAsync("data: Empty message cannot be sent.\n\n", cancellationToken);
            await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
            return;
        }

        try
        {
            await foreach (var chunk in _ragService.AskStreamingAsync(question, cancellationToken))
            {
                var escapedChunk = chunk.Replace("\n", "\\n").Replace("\r", "");
                await Response.WriteAsync($"data: {escapedChunk}\n\n", cancellationToken); // response stream'e yazar.
                await Response.Body.FlushAsync(cancellationToken); // Anında gönder, buffer bekleme!

            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected
        }
        catch (Exception ex)
        {
            await Response.WriteAsync($"data: Error: {ex.Message}\n\n", cancellationToken);
        }

        await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }
}
