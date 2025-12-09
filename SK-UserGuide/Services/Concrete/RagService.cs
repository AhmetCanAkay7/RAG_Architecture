using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Memory;
using SK_UserGuide.Services.Abstract;
using System.Collections;
using System.Text;

namespace SK_UserGuide.Services.Concrete;

public class RagService : IRagService
{
    private readonly ISemanticTextMemory _memory;
    private readonly Kernel _kernel;
    private const string CollectionName = "GuideCollection";
    private const int ChunkSize = 1000; // Her chunk 1000 karakter
    private const int Overlap = 200; // Chunk'lar arasında 200 karakter overlap

    public RagService(ISemanticTextMemory memory, Kernel kernel)
    {
        _memory = memory;
        _kernel = kernel;
    }

    public async Task<string> AskAsync(string question)
    {
        // question'ı embeddinge çevirdi ve gitti contexte aradı. en alakalı vektörleri getirdi.
        var results = _memory.SearchAsync(CollectionName,question,limit:3,minRelevanceScore:0.5);
        StringBuilder context = new StringBuilder();

        await foreach (var item in results)
        {
            context.AppendLine($"- {item.Metadata.Text}");
        }
        if (context.Length == 0) return "Dokümanlarda bu konuyla ilgili bilgi bulamadım.";

        // B. Prompt Oluştur
        var prompt = $@"
            Sen yardımcı bir asistansın. Aşağıdaki [BAĞLAM] bilgisini kullanarak cevap ver.
            
            [BAĞLAM]:
            {context}

            [SORU]: {question}
            
            [CEVAP]:";

        // C. Yapay Zekaya Gönder
        var result = await _kernel.InvokePromptAsync(prompt);
        return result.GetValue<string>();
    }

    public async Task AddDocumentAsync(string text, string baseId)
    {
        var chunks = ChunkText(text, ChunkSize, Overlap);
        for (int i = 0; i < chunks.Count; i++)
        {
            string chunkId = $"{baseId}_chunk_{i}";
            await _memory.SaveInformationAsync(CollectionName, chunks[i], chunkId);
        }
    }

    private List<string> ChunkText(string text, int chunkSize, int overlap)
    {
        var chunks = new List<string>();
        int start = 0;

        while (start < text.Length)
        {
            int end = Math.Min(start + chunkSize, text.Length);
            string chunk = text.Substring(start, end - start);
            chunks.Add(chunk);

            start += chunkSize - overlap;
            if (start >= text.Length) break;
        }

        return chunks;
    }
}
