using Microsoft.AspNetCore.Mvc;
using SK_UserGuide.Services.Abstract;

namespace SK_UserGuide.Controllers;

public class ChatController : Controller
{
    private readonly IRagService _ragService;

    public ChatController(IRagService ragService)
    {
        _ragService = ragService;
    }

    [HttpPost]
    public async Task<IActionResult> Ask(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return Json(new { success = false, answer = "Empty message cannot be sent." });
        }

        try
        {
            var answer = await _ragService.AskAsync(question);
            return Json(new { success = true, answer });
        }
        catch (Exception exception)
        {
            var errorMessage = $"Error: {exception.Message}";
            if (exception.InnerException != null)
            {
                errorMessage += $" | Inner: {exception.InnerException.Message}";
            }

            return Json(new
            {
                success = false,
                answer = errorMessage
            });
        }
    }

    /// <summary>
    /// Server-Sent Events endpoint for streaming LLM responses.
    /// </summary>
    [HttpGet]
    public async Task AskStreaming(string question, CancellationToken cancellationToken)
    {
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
                // Escape newlines for SSE format
                var escapedChunk = chunk.Replace("\n", "\\n").Replace("\r", "");
                await Response.WriteAsync($"data: {escapedChunk}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected, graceful exit
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
