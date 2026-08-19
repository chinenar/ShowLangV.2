using System.Globalization;

namespace ShowLangNative;

internal static class LanguageNames
{
    private static readonly IReadOnlyDictionary<ushort, string> Known =
        new Dictionary<ushort, string>
        {
            [0x041E] = "TH",
            [0x0409] = "EN",
            [0x0809] = "EN",
            [0x0411] = "JP",
            [0x0412] = "KO",
            [0x0804] = "ZH",
            [0x0404] = "ZH",
            [0x0C0A] = "ES",
            [0x040C] = "FR",
            [0x0407] = "DE",
            [0x0419] = "RU",
        };

    internal static string FromLanguageId(ushort languageId)
    {
        if (Known.TryGetValue(languageId, out string? name))
        {
            return name;
        }

        try
        {
            string iso = CultureInfo
                .GetCultureInfo(languageId)
                .TwoLetterISOLanguageName;

            return string.IsNullOrWhiteSpace(iso)
                ? languageId.ToString("X4")
                : iso.ToUpperInvariant();
        }
        catch (CultureNotFoundException)
        {
            return languageId.ToString("X4");
        }
    }
}
