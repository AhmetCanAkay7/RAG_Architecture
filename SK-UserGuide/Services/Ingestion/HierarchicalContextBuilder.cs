namespace SK_UserGuide.Services.Ingestion;

/// <summary>
/// Stack-based hierarchical context builder for heading tracking.
/// Maintains heading hierarchy and generates breadcrumb-style context prefixes.
/// </summary>
/// <example>
/// Given document headings:
///   # Introduction
///   ## Installation
///   ### Windows
///   ## Troubleshooting
/// 
/// Produces prefixes:
///   [Section: Introduction] >
///   [Section: Introduction > Installation] >
///   [Section: Introduction > Installation > Windows] >
///   [Section: Introduction > Troubleshooting] >
/// </example>
public class HierarchicalContextBuilder
{
    // Stack stores (Level, CleanText) tuples
    // Level = number of # characters (1 for #, 2 for ##, etc.)
    private readonly Stack<(int Level, string Text)> _headingStack = new();

    /// <summary>
    /// Process a heading line and update the hierarchy stack.
    /// Pops all headings at same or deeper level, then pushes new heading.
    /// </summary>
    /// <param name="line">Raw heading line (e.g., "## Installation")</param>
    public void ProcessHeading(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        // Detect heading level and clean text
        int currentLevel;
        string cleanText;

        if (line.TrimStart().StartsWith('#'))
        {
            // Markdown heading: count # symbols
            var trimmed = line.TrimStart();
            currentLevel = trimmed.TakeWhile(c => c == '#').Count();
            cleanText = trimmed.TrimStart('#', ' ').Trim();
        }
        else
        {
            // Non-markdown heading (numbered, all caps, etc.)
            // Treat as level 1 by default, text is the line itself
            currentLevel = 1;
            cleanText = line.Trim();
        }

        if (string.IsNullOrWhiteSpace(cleanText))
            return;

        // Pop headings at same or deeper level
        // Example: If stack has H3 and new heading is H2, pop H3
        while (_headingStack.Count > 0 && _headingStack.Peek().Level >= currentLevel)
        {
            _headingStack.Pop();
        }

        // Push new heading
        _headingStack.Push((currentLevel, cleanText));
    }

    /// <summary>
    /// Build context prefix in breadcrumb format.
    /// </summary>
    /// <returns>Format: "[Section: Main > Sub > SubSub] > " or empty if no headings</returns>
    public string BuildContextPrefix()
    {
        if (_headingStack.Count == 0)
            return string.Empty;

        // Stack is LIFO, so Reverse() to get root-to-leaf order
        var path = string.Join(" > ", _headingStack.Reverse().Select(x => x.Text));
        return $"[Section: {path}] > ";
    }

    /// <summary>
    /// Get the current (deepest) section title for metadata.
    /// </summary>
    /// <returns>Most recent heading text, or empty if none</returns>
    public string GetCurrentSectionTitle()
    {
        if (_headingStack.Count == 0)
            return string.Empty;

        // Return the deepest (most recent) heading
        return _headingStack.Peek().Text;
    }

    /// <summary>
    /// Get the full hierarchical section path for metadata.
    /// </summary>
    /// <returns>Full path like "Introduction > Installation > Windows"</returns>
    public string GetFullSectionPath()
    {
        if (_headingStack.Count == 0)
            return string.Empty;

        return string.Join(" > ", _headingStack.Reverse().Select(x => x.Text));
    }

    /// <summary>
    /// Reset the builder for a new document.
    /// </summary>
    public void Reset()
    {
        _headingStack.Clear();
    }

    /// <summary>
    /// Check if any headings have been processed.
    /// </summary>
    public bool HasContext => _headingStack.Count > 0;
}
