using System.Collections.Generic;

namespace AiTextCorrector.Localization;

/// <summary>
/// Table de référence des textes de l'interface, en anglais. Toute autre langue en
/// est une traduction : le français, intégré (<see cref="FrenchStrings"/>), et les
/// langues traduites par l'IA, qui reçoivent ces phrases telles quelles.
///
/// La clé dit où le texte apparaît ; elle sert aussi de contexte au traducteur.
/// {0}, {1}… sont remplacés à l'affichage (<see cref="Loc.Format"/>).
/// </summary>
internal static class EnglishStrings
{
    // Initialiseur par Add : une clé en double fait échouer le démarrage, au lieu
    // d'écraser silencieusement la première.
    public static IReadOnlyDictionary<string, string> All { get; } = new Dictionary<string, string>
    {
        // Messages d'erreur de l'application.
        { "App.InstallCheckFailed", "Installation check failed." },
        { "App.StartFailed", "Could not start {0}." },
        { "App.UnexpectedError", "An unexpected error occurred." },

        // Menu de la zone de notification et bulles.
        { "Tray.Open", "Open" },
        { "Tray.RunWithWindows", "Run with Windows" },
        { "Tray.Quit", "Quit" },
        { "Tray.Installed", "Installed. Open it from any application with {0}." },
        { "Tray.Updated", "Updated to version {0}." },

        // Fenêtre principale.
        { "Main.SettingsTooltip", "Settings: shortcut, API keys, prompts, language" },
        { "Main.Provider", "Provider:" },
        { "Main.AutoCopy", "Auto copy after correction" },
        { "Main.AsciiOnly", "Plain ASCII mode" },
        { "Main.AutoPasteBack", "Paste back automatically" },
        { "Main.SourceText", "Source text" },
        { "Main.PasteFromClipboard", "Paste from clipboard" },
        { "Main.Clear", "Clear" },
        { "Main.ExplainedCorrection", "Correction" },
        { "Main.ExplainedCorrectionTooltip", "Strict correction that shows each error in red and its correction in green here, with the rule on hover" },
        { "Main.InputPlaceholder", "Type or paste your text here" },
        { "Main.CorrectedText", "Corrected text" },
        { "Main.CopyResult", "Copy result" },
        { "Main.PasteBack", "Paste back into the app" },
        { "Main.OutputPlaceholder", "The corrected text will appear here" },
        { "Main.StrictCorrection", "Strict correction" },
        { "Main.CorrectionAndRewrite", "Correction + rewording" },
        { "Main.ProfessionalStyle", "Professional version" },
        { "Main.CustomPrompt", "Custom prompt" },
        { "Main.Translate", "Translate" },
        { "Main.TranslateTooltip", "Translate the corrected text into the selected language" },
        { "Main.TargetLanguageTooltip", "Target language" },
        { "Main.AddLanguageTooltip", "Add a language" },
        { "Main.RemoveLanguageTooltip", "Remove the selected language from the list" },

        // Barre d'état de la fenêtre principale.
        { "Status.Ready", "Ready" },
        { "Status.SettingsSaved", "Settings saved." },
        { "Status.SettingsNotWritten", "Settings applied, but they could not be written to disk." },
        { "Status.NoApiKey", "No API key configured. Click the gear button to add one." },
        { "Status.HotkeyUnavailable", "Could not register the shortcut ({0}): already used? Change it in Settings > Shortcut." },
        { "Status.HotkeySet", "Shortcut set: {0}." },
        { "Status.HotkeySetNotSaved", "Shortcut set: {0}, but it will not be kept at the next start." },
        { "Status.ClipboardLoadedAuto", "Clipboard text loaded automatically." },
        { "Status.NoTextToCorrect", "No text to correct." },
        { "Status.Correcting", "Correcting via {0}…" },
        { "Status.Corrected", "Corrected via {0}." },
        { "Status.ResultCopied", "Result copied to the clipboard." },
        { "Status.CopyFailed", "Copy to the clipboard failed." },
        { "Status.ApiError", "API error: {0}" },
        { "Status.CorrectingAndExplaining", "Correcting and explaining via {0}…" },
        { "Status.AutoPasteBackSkipped", "Auto paste-back is skipped so you can read the explanations." },
        { "Status.ExplanationsUnavailable", "Explanations unavailable for this answer: plain correction shown." },
        { "Status.TooManyChanges", "Too many changes to annotate." },
        { "Status.NoErrors", "No errors found." },
        { "Status.OneError", "1 error (red, fixed in green): hover it for the rule, click the text to edit it." },
        { "Status.Errors", "{0} errors (red, fixed in green): hover them for the rule, click the text to edit it." },
        { "Status.NoTextToTranslate", "No text to translate." },
        { "Status.Translating", "Translating into {0} via {1}…" },
        { "Status.Translated", "Translated into {0} via {1}." },
        { "Status.LanguageAdded", "{0} added to the list." },
        { "Status.LanguageAddedNotSaved", "{0} added, but it will not be kept after a restart." },
        { "Status.LanguageAlreadyListed", "{0} is already in the list." },
        { "Status.LanguageRemoved", "{0} removed from the list." },
        { "Status.LanguageRemovedNotSaved", "{0} removed, but it will be back after a restart." },
        { "Status.ClipboardLoaded", "Text loaded from the clipboard." },
        { "Status.NoResultToCopy", "No result to copy." },
        { "Status.PasteBackFailed", "Could not paste the text back: {0}" },
        { "Status.NoResultToPasteBack", "No result to paste back." },
        { "Status.PastedBack", "Text pasted back into the original application." },
        { "Status.OriginalWindowNotFound", "Original window not found, but the text is in the clipboard." },
        { "Status.SourceCleared", "Source text cleared." },
        { "Status.ResultCleared", "Corrected text cleared." },
        { "Status.ProviderSelected", "Active provider: {0}." },
        { "Status.ProviderNotSaved", "Active provider: {0}, but the configuration could not be saved." },

        // Texte annoté de la correction expliquée.
        { "Annotated.NoExplanation", "No explanation was provided for this change." },
        { "Annotated.Deleted", "(deleted)" },

        // Zone de recherche d'une langue.
        { "LanguageBox.Placeholder", "Type a language…" },
        { "LanguageBox.Tooltip", "Type the first letters, then Enter or click a suggestion" },

        // Fenêtre des paramètres.
        { "Settings.Title", "Settings" },
        { "Settings.ShortcutTab", "Shortcut" },
        { "Settings.ApiKeysTab", "API keys" },
        { "Settings.PromptsTab", "Prompts" },
        { "Settings.LanguageTab", "Language" },
        { "Settings.Cancel", "Cancel" },
        { "Settings.Save", "Save" },

        // Onglet du raccourci.
        { "Hotkey.Intro", "Shortcut that opens the window from any application and captures the selected text." },
        { "Hotkey.Help", "Click the field below, then press the combination you want: at least one modifier (Ctrl, Shift, Alt or Win) plus a key." },
        { "Hotkey.Placeholder", "Click here, then press the keys…" },
        { "Hotkey.Unavailable", "✗ Already used by another application or reserved by Windows. Choose another combination." },
        { "Hotkey.NeedsModifier", "✗ Add at least one modifier: Ctrl, Shift, Alt or Win." },
        { "Hotkey.Available", "✓ Available" },

        // Onglet des clés API.
        { "ApiKeys.Storage", "Keys are stored in your user registry (HKCU). An environment variable, if defined, takes precedence." },
        { "ApiKeys.Help", "Leave a field empty to keep the saved key. Type a value to replace it, or use Clear to delete it." },
        { "ApiKeys.OpenAi", "OpenAI key" },
        { "ApiKeys.Anthropic", "Anthropic / Claude key" },
        { "ApiKeys.Glm", "GLM (Z.ai) key" },
        { "ApiKeys.Groq", "Groq key" },
        { "ApiKeys.Clear", "Clear" },
        { "ApiKeys.Restore", "Restore" },
        { "ApiKeys.WillBeDeleted", "— will be deleted on save" },
        { "ApiKeys.KeySaved", "— key saved" },
        { "ApiKeys.NoKeySaved", "— no key saved" },
        { "ApiKeys.EnvironmentOverride", "Note: these environment variables are defined and take precedence as long as they exist: {0}." },

        // Onglet des prompts.
        { "Prompts.Intro", "Instruction sent to the AI, along with your text, by each button at the bottom of the main window." },
        { "Prompts.Reset", "Reset" },
        { "Prompts.ResetTooltip", "Restore the default prompt" },
        { "Prompts.ButtonLabel", "“{0}”" },
        { "Prompts.TranslationHint", "{language} is replaced by the language chosen next to the Translate button." },
        { "Prompts.Empty", "The prompt for {0} is empty. Type an instruction or click {1}." },

        // Onglet de la langue de l'interface.
        { "Language.Intro", "Language of the application's windows and messages." },
        { "Language.AddTooltip", "Add a language, translated by the AI" },
        { "Language.Note", "English and French are built in. Any other language is translated once by the provider selected in the main window, then saved on this computer for later use." },
        { "Language.ConfirmTitle", "Translate the interface into {0}?" },
        { "Language.ConfirmDetail", "{0} phrases will be sent to {1} for translation. The result is saved on this computer and reused afterwards." },
        { "Language.Translate", "Translate" },
        { "Language.Progress", "Translating with {0}… {1}/{2} phrases" },
        { "Language.Done", "{0} is ready. Click {1} to use it." },
        { "Language.Missing", "{0} phrases are not translated into this language yet and are shown in English." },
        { "Language.TranslateMissing", "Translate missing phrases" },
        { "Language.NoKey", "No API key for {0}: add one in the API keys tab, save, then try again." },
        { "Language.Failed", "Translation failed: {0}" },
        { "Language.NotSaved", "The translation could not be saved on this computer." },
        { "Language.NothingTranslated", "The AI answer contained no usable translation. Try again, or choose another provider." },

        // Installation et désinstallation.
        { "Setup.Title", "Install {0}" },
        { "Setup.NotInstalled", "{0} v{1} is not installed on this computer." },
        { "Setup.InstallLocation", "It will be installed for your account only, without administrator rights, in:" },
        { "Setup.StartWithWindows", "Start with Windows" },
        { "Setup.StartWithWindowsTooltip", "Keeps the shortcut available from the moment you log in" },
        { "Setup.LegacyCopy", "An older copy was found in {0}. It will no longer be used; you can delete it (administrator rights required)." },
        { "Setup.RunWithoutInstalling", "Run without installing" },
        { "Setup.RunWithoutInstallingTooltip", "Use this copy as is; nothing is written to your computer" },
        { "Setup.Install", "Install" },
        { "Setup.InstallFailed", "The installation failed." },
        { "Setup.UpdateFailed", "The update failed." },
        { "Setup.ConfirmUninstall", "Uninstall {0}?" },
        { "Setup.DeleteUserData", "Also delete your settings and API keys?\n\nThis cannot be undone: the API keys would have to be entered again. Choose No if you plan to reinstall later." },
        { "Setup.Uninstalled", "{0} has been uninstalled." },
        { "Setup.UninstallFailed", "The uninstallation failed." },

        // Erreurs des appels aux providers, affichées dans la barre d'état.
        { "Errors.MissingKey", "No {0} key. Enter it in Settings > API keys, or set {1}." },
        { "Errors.EmptyAnswer", "The {0} answer is empty." },
        { "Errors.HttpStatus", "{0} returned HTTP {1}" },
        { "Errors.HttpStatusDetail", "{0} returned HTTP {1}: {2}" },
        { "Errors.Http401", "The API key is invalid or revoked: check it in Settings > API keys." },
        { "Errors.Http403", "Access refused from this computer: check for a VPN, a company proxy or firewall, a country the provider does not serve, or another key set in an environment variable." },
        { "Errors.Http404", "Unknown model or address: check the model name in the configuration file." },
        { "Errors.Http429", "Rate limit or quota reached: wait a moment, or check your plan and credit." },
        { "Errors.Http5xx", "Temporary problem at the provider: try again in a moment." },
    };
}
