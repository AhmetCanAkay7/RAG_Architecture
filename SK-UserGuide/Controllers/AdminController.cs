using Microsoft.AspNetCore.Mvc;
using SK_UserGuide.Services.Abstract;
using SK_UserGuide.Services.Ingestion;

namespace SK_UserGuide.Controllers
{
    public class AdminController : Controller
    {
        private readonly IRagService _ragService;
        private readonly DocumentIngestionOrchestrator _ingestionOrchestrator;
        private readonly QdrantIngestionRepository _qdrantRepo;

        public AdminController(
            IRagService ragService,
            DocumentIngestionOrchestrator ingestionOrchestrator,
            QdrantIngestionRepository qdrantRepo)
        {
            _ragService = ragService;
            _ingestionOrchestrator = ingestionOrchestrator;
            _qdrantRepo = qdrantRepo;
        }

        public async Task<IActionResult> Index()
        {
            return await LoadIndexViewAsync();
        }

        /// <summary>
        /// Upload endpoint using the improved ingestion pipeline.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Lütfen bir dosya seçin.";
                return RedirectToAction("Index");
            }

            // Use new ingestion pipeline
            var result = await _ingestionOrchestrator.IngestAsync(file);

            if (result.Success)
            {
                // TempData ile mesajları taşı (PRG pattern)
                // Not: TempData sadece string, int, bool serialize edebilir
                TempData["Message"] = result.Message;
                TempData["Success"] = "true";
                TempData["ChunkCount"] = result.ChunkCount.ToString();
                TempData["Version"] = result.Version.ToString();
                TempData["ProcessingTime"] = result.ProcessingTimeMs.ToString();
            }
            else
            {
                TempData["Error"] = result.Message;
            }

            // Redirect to GET - bu sayede refresh yapıldığında POST tekrar gönderilmez
            return RedirectToAction("Index");
        }

        /// <summary>
        /// Helper method to load the Index view with documents.
        /// </summary>
        private async Task<IActionResult> LoadIndexViewAsync()
        {
            try
            {
                var documents = await _qdrantRepo.GetAllDocumentsAsync();
                ViewBag.Documents = documents;
            }
            catch
            {
                ViewBag.Documents = new List<DocumentInfo>();
            }

            return View("Index");
        }

        /// <summary>
        /// Delete a document by doc_id (removes ALL versions).
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> DeleteDocument(string docId)
        {
            if (string.IsNullOrWhiteSpace(docId))
            {
                return Json(new { success = false, message = "DocId gerekli." });
            }

            try
            {
                await _qdrantRepo.DeleteByDocIdAsync(docId);
                return Json(new { success = true, message = "Doküman başarıyla silindi." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Silme hatası: {ex.Message}" });
            }
        }

        /// <summary>
        /// Delete a specific version of a document.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> DeleteDocumentVersion(string docId, int version)
        {
            if (string.IsNullOrWhiteSpace(docId))
            {
                return Json(new { success = false, message = "DocId gerekli." });
            }

            try
            {
                await _qdrantRepo.DeleteByDocIdAndVersionAsync(docId, version);
                return Json(new { success = true, message = $"Doküman versiyon {version} başarıyla silindi." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Silme hatası: {ex.Message}" });
            }
        }

        /// <summary>
        /// Get list of all documents (JSON API).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetDocuments()
        {
            try
            {
                var documents = await _qdrantRepo.GetAllDocumentsAsync();
                return Json(new { success = true, documents });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message, documents = Array.Empty<object>() });
            }
        }

        /// <summary>
        /// Legacy upload endpoint (for backward compatibility).
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> UploadLegacy(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                ModelState.AddModelError("", "Lutfen bir dosya secin.");
                return await LoadIndexViewAsync();
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
                using var pdfReader = new iText.Kernel.Pdf.PdfReader(file.OpenReadStream());
                using var pdfDoc = new iText.Kernel.Pdf.PdfDocument(pdfReader);
                var strategy = new iText.Kernel.Pdf.Canvas.Parser.Listener.SimpleTextExtractionStrategy();
                for (int i = 1; i <= pdfDoc.GetNumberOfPages(); ++i)
                {
                    var page = pdfDoc.GetPage(i);
                    text += iText.Kernel.Pdf.Canvas.Parser.PdfTextExtractor.GetTextFromPage(page, strategy);
                }
            }
            else
            {
                ModelState.AddModelError("", "Sadece .txt ve .pdf dosyalari desteklenir.");
                return await LoadIndexViewAsync();
            }

            // Embedding ve store (legacy)
            string id = Guid.NewGuid().ToString();
            await _ragService.AddDocumentAsync(text, id);

            ViewBag.Message = "Dosya basariyla yuklendi ve islendi (legacy).";
            return await LoadIndexViewAsync();
        }
    }
}
