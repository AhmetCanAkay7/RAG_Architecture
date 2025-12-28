namespace SK_UserGuide.Services.LLM;

/// <summary>
/// Question types for response formatting.
/// </summary>
public enum QuestionType
{
    /// <summary>How to / Nasıl - Step by step instructions</summary>
    Procedure,

    /// <summary>Error / Hata - Problem causes and solutions</summary>
    Error,

    /// <summary>What is / Nedir - Definition and explanation</summary>
    Definition,

    /// <summary>When / Condition - Business rules and conditions</summary>
    Rule,

    /// <summary>General questions</summary>
    General
}

/// <summary>
/// Detects the type of question for appropriate response formatting.
/// </summary>
public static class QuestionTypeDetector
{
    /// <summary>
    /// Detect the type of question based on keywords and patterns.
    /// </summary>
    public static QuestionType Detect(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
            return QuestionType.General;

        var lower = question.ToLowerInvariant();

        // 1. Prosedür / Nasıl soruları
        if (ContainsAny(lower,
            "nasıl", "how to", "how do", "how can",
            "adımlar", "steps", "yapılır", "edilir",
            "kurulum", "install", "configure", "setup",
            "yapmak istiyorum", "want to"))
        {
            return QuestionType.Procedure;
        }

        // 2. Hata / Problem soruları
        if (ContainsAny(lower,
            "hata", "error", "err-", "err_",
            "sorun", "problem", "issue",
            "çalışmıyor", "not working", "doesn't work", "failed",
            "başarısız", "olmadı", "yapamıyorum", "cannot",
            "neden olmuyor", "why not"))
        {
            return QuestionType.Error;
        }

        // 3. Tanım soruları
        if (ContainsAny(lower,
            "nedir", "what is", "ne demek", "meaning",
            "tanım", "definition", "açıkla", "explain",
            "ne anlama", "anlamı"))
        {
            return QuestionType.Definition;
        }

        // 4. Kural / Koşul soruları
        if (ContainsAny(lower,
            "ne zaman", "when", "when should",
            "koşul", "condition", "şart",
            "gerekli", "required", "zorunlu", "mandatory",
            "yapılmalı", "should", "must", "have to",
            "izin", "permission", "yetki", "allowed"))
        {
            return QuestionType.Rule;
        }

        return QuestionType.General;
    }

    /// <summary>
    /// Get format hint for the question type.
    /// </summary>
    public static string GetFormatHint(QuestionType type, string language)
    {
        if (language == "TR")
        {
            return type switch
            {
                QuestionType.Procedure => "PROSEDÜR - Numaralı adımlar halinde yanıtla",
                QuestionType.Error => "HATA/SORUN - Olası nedenler ve çözüm adımları",
                QuestionType.Definition => "TANIM - Kısa ve net açıklama",
                QuestionType.Rule => "İŞ KURALI - Koşulları maddeler halinde listele",
                _ => "GENEL - Uygun formatta yanıtla"
            };
        }

        return type switch
        {
            QuestionType.Procedure => "PROCEDURE - Answer with numbered steps",
            QuestionType.Error => "ERROR/PROBLEM - List causes and solutions",
            QuestionType.Definition => "DEFINITION - Brief and clear explanation",
            QuestionType.Rule => "BUSINESS RULE - List conditions as bullet points",
            _ => "GENERAL - Answer in appropriate format"
        };
    }

    private static bool ContainsAny(string text, params string[] patterns)
    {
        return patterns.Any(p => text.Contains(p));
    }
}
