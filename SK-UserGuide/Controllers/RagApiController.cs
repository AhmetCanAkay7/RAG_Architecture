using Microsoft.AspNetCore.Mvc;
using SK_UserGuide.Models.Api;
using SK_UserGuide.Services.Abstract;

namespace SK_UserGuide.Controllers;

[ApiController]
[Route("api/rag")]
public partial class RagApiController : ControllerBase
{
    private readonly IRagService _ragService;
    private readonly ILogger<RagApiController> _logger;

    public RagApiController(IRagService ragService, ILogger<RagApiController> logger)
    {
        _ragService = ragService;
        _logger = logger;
    }

    [HttpPost("ask")]
    [ProducesResponseType(typeof(RagAnswerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Ask([FromBody] RagQuestionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest(new { error = "Question cannot be empty." });
        }

        try
        {
            _logger.LogInformation("RAG API request: {Question}", request.Question);

            // Collect streamed response
            var responseBuilder = new System.Text.StringBuilder();
            var sources = new List<SourceInfo>();
            var chunkCount = 0;

            await foreach (var chunk in _ragService.AskStreamingAsync(request.Question))
            {
                chunkCount++;
                responseBuilder.Append(chunk);
            }

            // Cache returns complete response as single chunk
            // Streaming LLM returns many small chunks
            var fromCache = chunkCount == 1 && responseBuilder.Length > 50;

            var fullResponse = responseBuilder.ToString();

            // Extract sources from response if present
            sources = ExtractSources(fullResponse);

            // Clean answer (remove source section for API response)
            var answer = CleanAnswer(fullResponse);

            _logger.LogInformation("RAG API response generated, length: {Length}, fromCache: {FromCache}",
                answer.Length, fromCache);

            return Ok(new RagAnswerResponse
            {
                Answer = answer,
                Sources = sources,
                FromCache = fromCache
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RAG API error for question: {Question}", request.Question);
            return StatusCode(500, new { error = "An error occurred processing your request." });
        }
    }

    /// <summary>
    /// Health check endpoint.
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }

    /// <summary>
    /// Extract source information from response text.
    /// </summary>
    private static List<SourceInfo> ExtractSources(string response)
    {
        var sources = new List<SourceInfo>();

        // Find sources section - format: "📚 **Sources:**"
        var sourcesIndex = response.IndexOf("**Sources:**", StringComparison.OrdinalIgnoreCase);
        if (sourcesIndex < 0)
            sourcesIndex = response.IndexOf("Sources:", StringComparison.OrdinalIgnoreCase);

        if (sourcesIndex < 0) return sources;

        var sourcesSection = response[sourcesIndex..];
        var lines = sourcesSection.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines.Skip(1)) // Skip "Sources:" header
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Parse format: "[1] DocName - Page X - Section"
            var match = SourceLineRegex().Match(trimmed);

            if (!match.Success) continue;

            var content = match.Groups[2].Value;
            var parts = content.Split(" - ", StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 1)
            {
                var docName = parts[0].Trim();
                string? section = null;
                int? page = null;

                // Parse remaining parts (Page X, Section)
                for (int i = 1; i < parts.Length; i++)
                {
                    var part = parts[i].Trim();
                    if (part.StartsWith("Page ", StringComparison.OrdinalIgnoreCase))
                    {
                        // Extract page number
                        var pageStr = part[5..].Trim();
                        if (int.TryParse(pageStr, out var pageNum))
                            page = pageNum;
                    }
                    else
                    {
                        section = part;
                    }
                }

                sources.Add(new SourceInfo
                {
                    DocName = docName,
                    SectionTitle = section,
                    Score = 0 // Score not available in this format
                });
            }
        }

        return sources;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^\[(\d+)\]\s*(.+)$")]
    private static partial System.Text.RegularExpressions.Regex SourceLineRegex();

    /// <summary>
    /// Remove sources section from answer for clean API response.
    /// </summary>
    private static string CleanAnswer(string response)
    {
        var sourcesIndex = response.IndexOf("**Sources:**", StringComparison.OrdinalIgnoreCase);
        if (sourcesIndex < 0)
            sourcesIndex = response.IndexOf("Sources:", StringComparison.OrdinalIgnoreCase);

        if (sourcesIndex > 0)
        {
            return response[..sourcesIndex].Trim();
        }

        return response.Trim();
    }
}
