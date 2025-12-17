using Microsoft.SemanticKernel;
using SK_UserGuide.Services.Abstract;
using System.Collections;
using System.Text;

namespace SK_UserGuide.Services.Concrete;

public class RagService : IRagService
{
    private readonly RagRetrievalService _retrievalService;
    private readonly RagIngestionService _ingestionService;

    public RagService(RagRetrievalService retrievalService, RagIngestionService ingestionService)
    {
        _retrievalService = retrievalService;
        _ingestionService = ingestionService;
    }

    public async Task<string> AskAsync(string question)
    {
        return await _retrievalService.AskAsync(question);
    }

    public async Task AddDocumentAsync(string text, string baseId)
    {
        var metadata = new Dictionary<string, object>
        {
            ["title"] = "User Guide",
            ["path"] = baseId,
            ["source_type"] = "uploaded"
        };
        await _ingestionService.IngestDocumentAsync(baseId, text, metadata);
    }
}
