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

    public RagService(ISemanticTextMemory memory, Kernel kernel)
    {
        _memory = memory;
        _kernel = kernel;
    }

    public async Task<string> AskAsync(string question)
    {
        // question'ı embeddinge çevirdi ve gitti contexte aradı. en alakalı vektörleri getirdi.
        var results = _memory.SearchAsync(CollectionName,question,limit:2,minRelevanceScore:0.5);
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

    // 1. Veri Yükleme (Uygulama açılınca çalışacak)
    public async Task InitAsync()
    {
        // Şimdilik manuel veri. İleride buraya PDF okuma kodunu koyacaksın.
        var docs = new Dictionary<string, string> {
                { "1", "Use OneDrive folder for internal file sharing." },
                { "2", "The VPN password must be changed every 3 months. You can change it from the IT portal." },
                { "3", "The dining hall is open from 12:00 PM to 1:30 PM. The menu is published online." }
            };

        // Volatile Memory (RAM) olduğu için her açılışta tekrar yüklüyoruz
        foreach (var doc in docs)
        {
            await _memory.SaveInformationAsync(CollectionName, doc.Value, doc.Key);
        }
    }
}
