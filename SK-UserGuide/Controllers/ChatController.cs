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
            return Json(new { 
                success = false, 
                answer = "Sistem şu an cevap veremiyor. Lütfen IT ile görüşün.",
                reason = exception.Message 
            });
        }
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }
}
