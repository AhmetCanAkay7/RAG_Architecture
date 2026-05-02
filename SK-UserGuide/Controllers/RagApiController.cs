using Microsoft.AspNetCore.Mvc;
using SK_UserGuide.Models.Api;
using SK_UserGuide.Services.Abstract;
using SK_UserGuide.Services.Concrete;

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

    [HttpPost("ask/stream")]
    public async Task AskStreaming([FromBody] RagQuestionRequest request, CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");
        Response.Headers.Append("X-Accel-Buffering", "no");

        if (string.IsNullOrWhiteSpace(request.Question))
        {
            await WriteSSEAsync("error", new { error = "Question cannot be empty." }, cancellationToken);
            await WriteSSEAsync("done", new { success = false }, cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            await WriteSSEAsync("error", new { error = "TenantId is required." }, cancellationToken);
            await WriteSSEAsync("done", new { success = false }, cancellationToken);
            return;
        }

        var tenantId = request.TenantId.Trim();

        try
        {
            _logger.LogInformation("RAG Streaming API request: {Question}, Tenant: {TenantId}",
                request.Question, tenantId);

            var fullResponse = new System.Text.StringBuilder();
            var sourcesStarted = false;
            var sourcesMarker = "---\n📚 **Sources:**";
            var sentLength = 0; // Track what we've actually sent

            // Stream each token as it arrives, but stop when sources section begins
            await foreach (var chunk in _ragService.AskStreamingAsync(request.Question, tenantId, cancellationToken))
            {
                fullResponse.Append(chunk);

                // Check if this chunk contains the sources marker
                var currentText = fullResponse.ToString();
                var sourcesIndex = currentText.IndexOf(sourcesMarker, StringComparison.Ordinal);

                if (sourcesIndex >= 0)
                {
                    // Sources section started - don't stream sources, we'll send them separately
                    if (!sourcesStarted)
                    {
                        sourcesStarted = true;
                        // Send any remaining text before sources marker that hasn't been sent yet
                        if (sourcesIndex > sentLength)
                        {
                            var remaining = currentText[sentLength..sourcesIndex].TrimEnd();
                            if (!string.IsNullOrEmpty(remaining))
                            {
                                await WriteSSEAsync("chunk", new { text = remaining }, cancellationToken);
                            }
                        }
                    }
                    // Skip streaming sources chunks
                }
                else if (!sourcesStarted)
                {
                    // Normal chunk - stream it
                    await WriteSSEAsync("chunk", new { text = chunk }, cancellationToken);
                    sentLength = currentText.Length; // Update sent position
                }
            }

            // Extract and send sources as structured data at the end
            var completeResponse = fullResponse.ToString();
            var sources = ExtractSources(completeResponse);

            if (sources.Count > 0)
            {
                await WriteSSEAsync("sources", new { sources }, cancellationToken);
            }

            await WriteSSEAsync("done", new { fromCache = false }, cancellationToken);

            _logger.LogInformation("RAG Streaming completed for: {Question}, Tenant: {TenantId}",
                request.Question, tenantId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("RAG Streaming cancelled by client");
        }
        catch (QdrantCollectionNotFoundException ex)
        {
            _logger.LogWarning(ex, "Tenant collection not found for streaming request: {TenantId}", tenantId);
            await WriteSSEAsync("error", new
            {
                error = "Tenant collection was not found. Create the tenant and upload documents before asking questions.",
                tenantId = ex.CollectionName
            }, cancellationToken);
            await WriteSSEAsync("done", new { success = false }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RAG Streaming error for question: {Question}", request.Question);
            await WriteSSEAsync("error", new { error = "An error occurred processing your request." }, cancellationToken);
            await WriteSSEAsync("done", new { success = false }, cancellationToken);
        }
    }
    private async Task WriteSSEAsync(string eventType, object? data, CancellationToken cancellationToken)
    {
        var json = data is null ? "{}" : System.Text.Json.JsonSerializer.Serialize(data);
        await Response.WriteAsync($"event: {eventType}\n", cancellationToken);
        await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }


    [HttpPost("ask")]
    [ProducesResponseType(typeof(RagAnswerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Ask([FromBody] RagQuestionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest(new { error = "Question cannot be empty." });
        }

        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            return BadRequest(new { error = "TenantId is required." });
        }

        var tenantId = request.TenantId.Trim();

        try
        {
            _logger.LogInformation("RAG API request: {Question}, Tenant: {TenantId}",
                request.Question, tenantId);

            // Collect streamed response
            var responseBuilder = new System.Text.StringBuilder();
            var sources = new List<SourceInfo>();

            await foreach (var chunk in _ragService.AskStreamingAsync(request.Question, tenantId))
            {
                responseBuilder.Append(chunk);
            }

            var fullResponse = responseBuilder.ToString();

            // Extract sources from response if present
            sources = ExtractSources(fullResponse);

            // Clean answer (remove source section for API response)
            var answer = CleanAnswer(fullResponse);

            _logger.LogInformation("RAG API response generated, length: {Length}, Tenant: {TenantId}",
                answer.Length, tenantId);

            return Ok(new RagAnswerResponse
            {
                Answer = answer,
                Sources = sources,
                FromCache = false
            });
        }
        catch (QdrantCollectionNotFoundException ex)
        {
            _logger.LogWarning(ex, "Tenant collection not found for question: {Question}", request.Question);
            return NotFound(new
            {
                error = "Tenant collection was not found. Create the tenant and upload documents before asking questions.",
                tenantId = ex.CollectionName
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RAG API error for question: {Question}", request.Question);
            return StatusCode(500, new { error = "An error occurred processing your request." });
        }
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }
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
