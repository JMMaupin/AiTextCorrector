using System;
using System.IO;
using System.Text.Json;
using AiTextCorrector.Models;

namespace AiTextCorrector.Services;

public class ConfigService
{
    private const string FileName = "appsettings.json";

    public AppConfig Load()
    {
        AppConfig config;

        if (File.Exists(FileName))
        {
            var json = File.ReadAllText(FileName);
            config = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new AppConfig();
        }
        else
        {
            config = new AppConfig();
            Save(config);
        }

        // Les clés API ne sont volontairement pas gérées ici : elles sont stockées
        // dans le registre via ApiKeyStore (et surchargeables par variable d'env).
        return config;
    }

    public void Save(AppConfig config)
    {
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FileName, json);
    }
}
