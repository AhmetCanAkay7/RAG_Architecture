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
            return Json(new { success = false, answer = "Boş mesaj gönderilemez." });
        }

        try
        {
            var answer = await _ragService.AskAsync(question);
            return Json(new { success = true, answer = answer });
        }
        catch (Exception exception)
        {
            // Show more details in development
            var errorMessage = $"Hata: {exception.Message}";
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

    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }
}
