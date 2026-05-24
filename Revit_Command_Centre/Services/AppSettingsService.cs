using System;
using System.IO;
using System.Text.Json;

namespace Revit_Command_Centre.Services
{
    /// <summary>
    /// Machine-level settings persisted to %APPDATA%\BIMCommandCentre\appsettings.json.
    /// These are the global defaults; per-project overrides live in ProjectConfig.
    /// </summary>
    public class AppSettings
    {
        public string DefaultFamilyOutputFolder { get; set; } = string.Empty;
        public string DefaultFamilyNamePrefix   { get; set; } = string.Empty;
        public string FamilyTemplateFolder      { get; set; } = string.Empty;
    }

    public static class AppSettingsService
    {
        private static readonly string SettingsDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BIMCommandCentre");

        private static readonly string SettingsPath = Path.Combine(SettingsDir, "appsettings.json");

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions)
                           ?? new AppSettings();
            }
            catch { }
            return new AppSettings();
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
            }
            catch { }
        }
    }
}
