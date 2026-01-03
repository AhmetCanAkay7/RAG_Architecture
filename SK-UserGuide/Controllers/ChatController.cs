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
    public async Task<IActionResult> Ask(string question, string? sessionId = null)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return Json(new { success = false, answer = "Empty message cannot be sent." });
        }

        // Generate sessionId if not provided (for new conversations)
        sessionId ??= HttpContext.Session.Id;

        try
        {
            var answer = await _ragService.AskAsync(question, sessionId);
            return Json(new { success = true, answer = answer, sessionId = sessionId });
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

    [HttpPost]
    public IActionResult ClearHistory(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return Json(new { success = false, message = "Session ID required." });
        }

        _ragService.ClearChatHistory(sessionId);
        return Json(new { success = true, message = "Chat history cleared." });
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }
}
