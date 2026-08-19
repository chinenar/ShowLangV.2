using System.IO;
using System.Text.Json;

namespace ShowLangNative;

internal sealed class ShowLangSettings
{
    internal const int DefaultScalePercent = 100;
    internal const int DefaultOpacityPercent = 100;

    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
        "ShowLang");

    private static readonly string SettingsPath = Path.Combine(
        SettingsDirectory,
        "settings.json");

    public int ScalePercent { get; set; } = DefaultScalePercent;
    public int OpacityPercent { get; set; } = DefaultOpacityPercent;

    internal static ShowLangSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new ShowLangSettings();
            }

            string json = File.ReadAllText(SettingsPath);
            ShowLangSettings? settings = JsonSerializer.Deserialize<ShowLangSettings>(
                json);
            settings ??= new ShowLangSettings();
            settings.Normalize();
            return settings;
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
            return new ShowLangSettings();
        }
    }

    internal void Save()
    {
        try
        {
            Normalize();
            Directory.CreateDirectory(SettingsDirectory);
            string json = JsonSerializer.Serialize(
                this,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                });
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
        }
    }

    internal void ResetAppearance()
    {
        ScalePercent = DefaultScalePercent;
        OpacityPercent = DefaultOpacityPercent;
    }

    private void Normalize()
    {
        ScalePercent = Math.Clamp(ScalePercent, 60, 200);
        OpacityPercent = Math.Clamp(OpacityPercent, 40, 100);
    }
}
