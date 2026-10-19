using System.Reflection;

namespace AiTextCorrector;

/// <summary>
/// Source unique du nom affiché par l'application. Le titre était auparavant codé
/// en dur à trois endroits (fenêtre, barre de titre, zone de notification), qui ont
/// fini par diverger : le commit de refactorisation a rétrogradé les deux titres
/// XAML en « V1 » alors que la zone de notification affichait toujours « V3 ».
/// Le numéro provient désormais de la propriété Version du csproj.
/// </summary>
internal static class AppInfo
{
    public static string Product { get; } = ReadProduct();

    /// <summary>Version SemVer, statut de pré-version compris (« 1.2.0-beta »).</summary>
    public static string Version { get; } = ReadVersion();

    public static string DisplayName { get; } =
        string.IsNullOrWhiteSpace(Version) ? Product : $"{Product} v{Version}";

    private static string ReadProduct()
    {
        var product = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyProductAttribute>()?.Product;
        return string.IsNullOrWhiteSpace(product) ? "AI Text Corrector" : product;
    }

    private static string ReadVersion()
    {
        var assembly = Assembly.GetEntryAssembly();

        // AssemblyInformationalVersion porte la version SemVer complète, suffixe de
        // pré-version compris (« 1.2.0-beta »), là où AssemblyVersion la tronque en
        // 1.2.0.0 et perdrait le statut bêta. Le SDK y ajoute « +<sha git> » :
        // cette métadonnée n'a pas sa place dans un titre de fenêtre, on la coupe.
        var version = assembly?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion?
            .Split('+')[0];

        if (string.IsNullOrWhiteSpace(version))
        {
            version = assembly?.GetName().Version?.ToString(3);
        }

        return version ?? string.Empty;
    }
}
