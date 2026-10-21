using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AiTextCorrector.Localization;
using AiTextCorrector.Models;
using AiTextCorrector.Services;

namespace AiTextCorrector.Settings;

/// <summary>
/// Onglet « Language » : langue de l'interface. L'anglais et le français sont
/// intégrés ; une autre langue, cherchée par son nom, est traduite par l'IA après
/// confirmation, puis enregistrée sur le poste pour les fois suivantes.
/// </summary>
public partial class LanguageSettingsPanel : UserControl
{
    // Rouge des messages d'erreur des autres onglets.
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));

    private AppConfig _config = new();
    private ApiKeyStore _keyStore = new();
    private MultiProviderCorrectionService? _aiService;

    // Langue cherchée, en attente de confirmation.
    private string? _pendingLanguage;

    public LanguageSettingsPanel()
    {
        InitializeComponent();

        // Référence vivante : les langues déjà dans la liste disparaissent des suggestions.
        AddLanguageBox.ExcludedLanguages = ListedLanguages();
    }

    /// <summary>Vrai pendant une traduction : l'enregistrement des réglages attend sa fin.</summary>
    public bool IsBusy { get; private set; }

    public event EventHandler? BusyChanged;

    /// <summary>Langue choisie (nom anglais).</summary>
    public string Selected =>
        (LanguageComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? Localizer.Instance.Language;

    public void Load(AppConfig config, ApiKeyStore keyStore, MultiProviderCorrectionService aiService)
    {
        _config = config;
        _keyStore = keyStore;
        _aiService = aiService;
        RefreshList(Localizer.Instance.Language);
    }

    // Langues intégrées d'abord, puis celles déjà traduites sur ce poste.
    private void RefreshList(string selected)
    {
        LanguageComboBox.Items.Clear();
        foreach (var language in Localizer.BuiltInLanguages.Concat(InterfaceTranslationStore.SavedLanguages()))
        {
            LanguageComboBox.Items.Add(new ComboBoxItem { Content = LanguageCatalog.Describe(language), Tag = language });
        }

        Select(selected);
    }

    private void Select(string language)
    {
        var index = ListedLanguages().ToList().FindIndex(l => string.Equals(l, language, StringComparison.OrdinalIgnoreCase));
        LanguageComboBox.SelectedIndex = Math.Max(index, 0);
    }

    private IEnumerable<string> ListedLanguages() =>
        LanguageComboBox.Items.OfType<ComboBoxItem>().Select(i => (string)i.Tag);

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowNotice(null);

    /// <summary>
    /// Message du dernier essai éventuel, suivi du nombre de phrases encore en anglais
    /// dans la langue choisie, avec de quoi les traduire.
    /// </summary>
    private void ShowNotice(string? message, bool isError = false)
    {
        var missing = Localizer.IsBuiltIn(Selected)
            ? 0
            : InterfaceTranslationStore.Load(Selected)?.MissingPhrases().Count ?? 0;

        var parts = new List<string>();
        if (message is not null) parts.Add(message);
        if (missing > 0) parts.Add(Loc.Format("Language.Missing", missing));

        NoticeText.Text = string.Join(" ", parts);
        NoticeText.Visibility = parts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (isError)
        {
            NoticeText.Foreground = ErrorBrush;
        }
        else
        {
            NoticeText.ClearValue(TextBlock.ForegroundProperty);
        }

        TranslateMissingButton.Visibility = missing > 0 && !IsBusy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddLanguageButton_Click(object sender, RoutedEventArgs e)
    {
        // Le bouton bascule : un second clic referme la zone de saisie.
        if (AddLanguageBox.Visibility == Visibility.Visible)
        {
            HideAddLanguageBox();
            return;
        }

        LanguageComboBox.Visibility = Visibility.Collapsed;
        AddLanguageBox.Visibility = Visibility.Visible;
        AddLanguageBox.FocusInput();
    }

    private void AddLanguageBox_LanguageSubmitted(object? sender, string language)
    {
        HideAddLanguageBox();

        // Langue intégrée ou déjà traduite : rien à demander à l'IA.
        var existing = ListedLanguages().FirstOrDefault(l => string.Equals(l, language, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            ConfirmCard.Visibility = Visibility.Collapsed;
            Select(existing);
            return;
        }

        _pendingLanguage = language;
        ConfirmTitleText.Text = Loc.Format("Language.ConfirmTitle", language);
        ConfirmDetailText.Text = Loc.Format("Language.ConfirmDetail", EnglishStrings.All.Count, _config.Provider);
        ConfirmCard.Visibility = Visibility.Visible;
    }

    private void AddLanguageBox_Cancelled(object? sender, EventArgs e)
    {
        HideAddLanguageBox();
        AddLanguageButton.Focus();
    }

    private void HideAddLanguageBox()
    {
        AddLanguageBox.Reset();
        AddLanguageBox.Visibility = Visibility.Collapsed;
        LanguageComboBox.Visibility = Visibility.Visible;
    }

    private void CancelTranslation_Click(object sender, RoutedEventArgs e)
    {
        _pendingLanguage = null;
        ConfirmCard.Visibility = Visibility.Collapsed;
    }

    private async void ConfirmTranslation_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingLanguage is not { } language) return;

        await TranslateAsync(new InterfaceTranslation { Language = language }, EnglishStrings.All);
    }

    // Après une mise à jour de l'application : phrases nouvelles ou modifiées seulement.
    private async void TranslateMissing_Click(object sender, RoutedEventArgs e)
    {
        if (InterfaceTranslationStore.Load(Selected) is not { } translation) return;

        await TranslateAsync(translation, translation.MissingPhrases());
    }

    // Appelée par des gestionnaires async void : aucune exception ne doit en sortir.
    private async Task TranslateAsync(InterfaceTranslation translation, IReadOnlyDictionary<string, string> phrases)
    {
        if (_aiService is null || phrases.Count == 0) return;

        // Provider de la fenêtre principale ; une clé saisie dans l'onglet voisin
        // mais pas encore enregistrée n'est pas utilisable.
        var provider = _config.Provider;
        if (!_keyStore.HasKeyFor(provider))
        {
            ShowNotice(Loc.Format("Language.NoKey", provider), isError: true);
            return;
        }

        SetBusy(true);
        TranslationProgress.Maximum = phrases.Count;
        TranslationProgress.Value = 0;
        ProgressText.Text = Loc.Format("Language.Progress", provider, 0, phrases.Count);

        var translatedCount = 0;
        var saved = true;
        string? error = null;
        try
        {
            await _aiService.TranslateInterfaceAsync(phrases, translation.Language, (processed, batch) =>
            {
                foreach (var (key, text) in batch)
                {
                    translation.Phrases[key] = new TranslatedPhrase { Source = phrases[key], Text = text };
                }

                // Enregistré lot par lot : une erreur plus loin ne fait rien perdre.
                if (batch.Count > 0)
                {
                    translatedCount += batch.Count;
                    saved = InterfaceTranslationStore.Save(translation);
                }

                TranslationProgress.Value = processed;
                ProgressText.Text = Loc.Format("Language.Progress", provider, processed, phrases.Count);
            });
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        finally
        {
            SetBusy(false);
        }

        // Nouvelle langue : elle apparaît dans la liste, choisie.
        if (translatedCount > 0 && saved)
        {
            RefreshList(translation.Language);
        }

        if (error is not null)
        {
            ShowNotice(Loc.Format("Language.Failed", error), isError: true);
        }
        else if (!saved)
        {
            ShowNotice(Loc.Text("Language.NotSaved"), isError: true);
        }
        else if (translatedCount == 0)
        {
            ShowNotice(Loc.Text("Language.NothingTranslated"), isError: true);
        }
        else
        {
            ShowNotice(Loc.Format("Language.Done", translation.Language, Loc.Text("Settings.Save")));
        }
    }

    private void SetBusy(bool busy)
    {
        IsBusy = busy;
        LanguageComboBox.IsEnabled = !busy;
        AddLanguageButton.IsEnabled = !busy;
        ProgressPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

        if (busy)
        {
            HideAddLanguageBox();
            _pendingLanguage = null;
            ConfirmCard.Visibility = Visibility.Collapsed;
            NoticeText.Visibility = Visibility.Collapsed;
            TranslateMissingButton.Visibility = Visibility.Collapsed;
        }

        BusyChanged?.Invoke(this, EventArgs.Empty);
    }
}
