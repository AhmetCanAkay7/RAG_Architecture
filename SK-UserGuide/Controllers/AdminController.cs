using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SK_UserGuide.Configuration;
using SK_UserGuide.Services.Abstract;
using SK_UserGuide.Services.Concrete;
using SK_UserGuide.Services.Ingestion;

namespace SK_UserGuide.Controllers
{
    public class AdminController : Controller
    {
        private readonly IDocumentIngestionOrchestrator _ingestionOrchestrator;
        private readonly IQdrantIngestionRepository _qdrantRepo;
        private readonly QdrantRestClient _qdrantClient;
        private readonly QdrantSettings _qdrantSettings;

        public AdminController(
            IDocumentIngestionOrchestrator ingestionOrchestrator,
            IQdrantIngestionRepository qdrantRepo,
            QdrantRestClient qdrantClient,
            IOptions<QdrantSettings> qdrantOptions)
        {
            _ingestionOrchestrator = ingestionOrchestrator;
            _qdrantRepo = qdrantRepo;
            _qdrantClient = qdrantClient;
            _qdrantSettings = qdrantOptions.Value;
        }

        public async Task<IActionResult> Index(string? tenantId = null)
        {
            tenantId ??= _qdrantSettings.DefaultCollection;
            ViewBag.TenantId = tenantId;
            return await LoadIndexViewAsync(tenantId);
        }

        /// <summary>
        /// Upload a document to a specific tenant collection.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile file, string tenantId = "default")
        {
            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Please select a file.";
                return RedirectToAction("Index", new { tenantId });
            }

            var result = await _ingestionOrchestrator.IngestAsync(file, tenantId);

            if (result.Success)
            {
                TempData["Message"] = result.Message;
                TempData["Success"] = "true";
                TempData["ChunkCount"] = result.ChunkCount.ToString();
                TempData["ProcessingTime"] = result.ProcessingTimeMs.ToString();
            }
            else
            {
                TempData["Error"] = result.Message;
            }

            // Redirect to GET - prevents POST resubmission on refresh
            return RedirectToAction("Index", new { tenantId });
        }

        private async Task<IActionResult> LoadIndexViewAsync(string tenantId)
        {
            try
            {
                var documents = await _qdrantRepo.GetAllDocumentsAsync(tenantId);
                ViewBag.Documents = documents;
            }
            catch
            {
                ViewBag.Documents = new List<DocumentInfo>();
            }

            return View("Index");
        }

        /// <summary>
        /// Delete a document by doc_id from a specific tenant collection.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> DeleteDocument(string docId, string tenantId = "default")
        {
            if (string.IsNullOrWhiteSpace(docId))
            {
                return Json(new { success = false, message = "DocId is required." });
            }

            try
            {
                await _qdrantRepo.DeleteByDocIdAsync(tenantId, docId);
                return Json(new { success = true, message = "Document deleted successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Delete error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Get list of all documents in a tenant collection (JSON API).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetDocuments(string tenantId = "default")
        {
            try
            {
                var documents = await _qdrantRepo.GetAllDocumentsAsync(tenantId);
                return Json(new { success = true, documents, tenantId });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message, documents = Array.Empty<object>() });
            }
        }

        // ──────────────────────────────────────────────
        // Tenant Management API Endpoints
        // ──────────────────────────────────────────────

        /// <summary>
        /// Create a new tenant (Qdrant collection).
        /// POST /Admin/CreateTenant?tenantId=hr-bot
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateTenant(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return Json(new { success = false, message = "TenantId is required." });
            }

            try
            {
                await _qdrantRepo.EnsureCollectionAsync(tenantId);
                return Json(new { success = true, message = $"Tenant '{tenantId}' created successfully.", tenantId });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error creating tenant: {ex.Message}" });
            }
        }

        /// <summary>
        /// List all tenants (Qdrant collections).
        /// GET /Admin/ListTenants
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> ListTenants()
        {
            try
            {
                var collections = await _qdrantClient.ListCollectionsAsync();
                return Json(new { success = true, tenants = collections });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message, tenants = Array.Empty<string>() });
            }
        }

        /// <summary>
        /// Delete a tenant (Qdrant collection) and all its data.
        /// POST /Admin/DeleteTenant?tenantId=hr-bot
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> DeleteTenant(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return Json(new { success = false, message = "TenantId is required." });
            }

            try
            {
                await _qdrantClient.DeleteCollectionAsync(tenantId);
                return Json(new { success = true, message = $"Tenant '{tenantId}' deleted successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error deleting tenant: {ex.Message}" });
            }
        }
    }
}
