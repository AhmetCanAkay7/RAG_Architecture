using Microsoft.AspNetCore.Mvc;
using SK_UserGuide.Services.Abstract;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace SK_UserGuide.Controllers
{
    public class AdminController : Controller
    {
        private readonly IRagService _ragService;

        public AdminController(IRagService ragService)
        {
            _ragService = ragService;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                ModelState.AddModelError("", "Lutfen bir dosya secin.");
                return View("Index");
            }

            string text = "";
            string extension = Path.GetExtension(file.FileName).ToLower();

            if (extension == ".txt")
            {
                using (var reader = new StreamReader(file.OpenReadStream()))
                {
                    text = await reader.ReadToEndAsync();
                }
            }
            else if (extension == ".pdf")
            {
                using (var pdfReader = new PdfReader(file.OpenReadStream()))
                using (var pdfDoc = new PdfDocument(pdfReader))
                {
                    var strategy = new iText.Kernel.Pdf.Canvas.Parser.Listener.SimpleTextExtractionStrategy();
                    for (int i = 1; i <= pdfDoc.GetNumberOfPages(); ++i)
                    {
                        var page = pdfDoc.GetPage(i);
                        text += PdfTextExtractor.GetTextFromPage(page, strategy);
                    }
                }
            }
            else
            {
                ModelState.AddModelError("", "Sadece .txt ve .pdf dosyalari desteklenir.");
                return View("Index");
            }

            // Embedding ve store
            string id = Guid.NewGuid().ToString();
            await _ragService.AddDocumentAsync(text, id);

            ViewBag.Message = "Dosya basariyla yuklendi ve islendi.";
            return View("Index");
        }
    }
}
