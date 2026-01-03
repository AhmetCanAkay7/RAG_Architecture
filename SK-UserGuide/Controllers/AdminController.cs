using Microsoft.AspNetCore.Mvc;
using SK_UserGuide.Services.Abstract;
using SK_UserGuide.Services.Ingestion;

namespace SK_UserGuide.Controllers
{
    public class AdminController : Controller
    {
        private readonly IDocumentIngestionOrchestrator _ingestionOrchestrator;
        private readonly IQdrantIngestionRepository _qdrantRepo;

        public AdminController(
            IDocumentIngestionOrchestrator ingestionOrchestrator,
            IQdrantIngestionRepository qdrantRepo)
        {
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
                TempData["Error"] = "Please select a file.";
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
                return Json(new { success = false, message = "DocId is required." });
            }

            try
            {
                await _qdrantRepo.DeleteByDocIdAsync(docId);
                return Json(new { success = true, message = "Document deleted successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Delete error: {ex.Message}" });
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
                return Json(new { success = false, message = "DocId is required." });
            }

            try
            {
                await _qdrantRepo.DeleteByDocIdAndVersionAsync(docId, version);
                return Json(new { success = true, message = $"Document version {version} deleted successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Delete error: {ex.Message}" });
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
    }
}
