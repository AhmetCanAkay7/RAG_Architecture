using System.Globalization;
using System.Text;

namespace SK_UserGuide.Services.Retrieval;

public static class SearchTextNormalizer
{
    public static string ToSearchText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var lower = text.ToLower(new CultureInfo("tr-TR"));
        var sb = new StringBuilder(lower.Length);

        foreach (var c in lower)
        {
            sb.Append(c switch
            {
                'ç' => 'c',
                'ğ' => 'g',
                'ı' => 'i',
                'i' => 'i',
                'ö' => 'o',
                'ş' => 's',
                'ü' => 'u',
                _ => c
            });
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
