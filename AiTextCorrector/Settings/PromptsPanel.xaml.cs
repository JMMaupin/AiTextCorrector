using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AiTextCorrector.Models;

namespace AiTextCorrector.Settings;

/// <summary>Un prompt modifiable, rattaché au bouton qui l'utilise.</summary>
public sealed class PromptEntry : INotifyPropertyChanged
{
    private readonly Action<PromptSet, string> _store;
    private string _text;

    public PromptEntry(string label, string text, string defaultText, Action<PromptSet, string> store, string? hint = null)
    {
        Label = label;
        _text = text;
        DefaultText = defaultText;
        _store = store;
        Hint = hint;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Label { get; }
    public string DefaultText { get; }
    public string? Hint { get; }
    public Visibility HintVisibility => Hint is null ? Visibility.Collapsed : Visibility.Visible;

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
        }
    }

    public void StoreIn(PromptSet prompts) => _store(prompts, Text);
}

/// <summary>Onglet « Prompts » : instructions envoyées par les boutons de la fenêtre principale.</summary>
public partial class PromptsPanel : UserControl
{
    private List<PromptEntry> _entries = new();

    public PromptsPanel()
    {
        InitializeComponent();
    }

    // Libellés identiques à ceux des boutons de la fenêtre principale : c'est par
    // eux que l'utilisateur reconnaît le prompt à modifier.
    public void Load(PromptSet prompts)
    {
        var defaults = new PromptSet();
        _entries = new List<PromptEntry>
        {
            new("« Correction stricte »", prompts.StrictCorrection, defaults.StrictCorrection, (p, v) => p.StrictCorrection = v),
            new("« Correction + reformulation »", prompts.CorrectionAndRewrite, defaults.CorrectionAndRewrite, (p, v) => p.CorrectionAndRewrite = v),
            new("« Version pro »", prompts.ProfessionalStyle, defaults.ProfessionalStyle, (p, v) => p.ProfessionalStyle = v),
            new("« Prompt perso »", prompts.CustomPrompt, defaults.CustomPrompt, (p, v) => p.CustomPrompt = v),
            new("« Translate »", prompts.Translation, defaults.Translation, (p, v) => p.Translation = v,
                hint: "{language} is replaced by the language chosen next to the Translate button."),
        };
        PromptList.ItemsSource = _entries;
    }

    /// <summary>
    /// Vrai si tous les prompts sont renseignés. Sinon, signale le premier vide :
    /// un bouton sans instruction enverrait le texte seul, sans dire quoi en faire.
    /// </summary>
    public bool Validate()
    {
        var empty = _entries.FirstOrDefault(e => string.IsNullOrWhiteSpace(e.Text));
        ErrorTextBlock.Text = empty is null ? string.Empty : $"The prompt for {empty.Label} is empty. Type an instruction or click Reset.";
        ErrorTextBlock.Visibility = empty is null ? Visibility.Collapsed : Visibility.Visible;
        return empty is null;
    }

    public void StoreIn(PromptSet prompts)
    {
        foreach (var entry in _entries)
        {
            entry.StoreIn(prompts);
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PromptEntry entry)
        {
            entry.Text = entry.DefaultText;
        }
    }
}
