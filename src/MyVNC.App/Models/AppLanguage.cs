namespace MyVNC.App.Models;

public enum AppLanguage
{
    Swedish,
    Norwegian,
    Danish,
    Finnish,
    Icelandic,
    English,
}

public static class AppLanguageExtensions
{
    /// <summary>The language's own name for itself (endonym) — language pickers conventionally
    /// show these rather than translating the name into the currently active UI language.</summary>
    public static string NativeName(this AppLanguage language) => language switch
    {
        AppLanguage.Swedish => "Svenska",
        AppLanguage.Norwegian => "Norsk",
        AppLanguage.Danish => "Dansk",
        AppLanguage.Finnish => "Suomi",
        AppLanguage.Icelandic => "Íslenska",
        AppLanguage.English => "English",
        _ => language.ToString(),
    };
}
