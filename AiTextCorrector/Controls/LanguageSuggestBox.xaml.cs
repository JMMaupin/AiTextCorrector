using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AiTextCorrector.Services;

namespace AiTextCorrector.Controls;

/// <summary>
/// Zone de saisie d'une langue, avec suggestions au fil de la frappe.
///
/// L'AutoSuggestBox de WPF-UI 3.0.5 ne convient pas : chaque déplacement aux
/// flèches y déclenche SuggestionChosen et réécrit la saisie, et Entrée sur une
/// liste sans sélection lève une exception.
/// </summary>
public partial class LanguageSuggestBox : UserControl
{
    public LanguageSuggestBox()
    {
        InitializeComponent();
    }

    /// <summary>Langues à ne plus proposer : celles déjà présentes dans la liste.</summary>
    public IEnumerable<string> ExcludedLanguages { get; set; } = Array.Empty<string>();

    /// <summary>Langue validée : nom anglais du catalogue, ou saisie libre.</summary>
    public event EventHandler<string>? LanguageSubmitted;

    /// <summary>Saisie abandonnée (Échap alors qu'aucune suggestion n'est affichée).</summary>
    public event EventHandler? Cancelled;

    public void Reset()
    {
        QueryTextBox.Clear();
        ClosePopup();
    }

    public void FocusInput() =>
        // Différé : juste après le passage de Collapsed à Visible, la zone n'est pas
        // encore mise en page et refuse le focus.
        Dispatcher.InvokeAsync(() => { QueryTextBox.Focus(); }, DispatcherPriority.Input);

    private void QueryTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var suggestions = LanguageCatalog.Suggest(QueryTextBox.Text, ExcludedLanguages);
        SuggestionList.ItemsSource = suggestions;

        // Première suggestion présélectionnée : Entrée la valide directement.
        SuggestionList.SelectedIndex = suggestions.Count > 0 ? 0 : -1;
        SuggestionPopup.IsOpen = suggestions.Count > 0;
    }

    private void QueryTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
            case Key.Up:
                if (!SuggestionPopup.IsOpen) return;
                MoveSelection(e.Key == Key.Down ? 1 : -1);
                e.Handled = true;
                break;

            case Key.Enter:
                Submit(SuggestionPopup.IsOpen ? SuggestionList.SelectedItem as LanguageSuggestion : null);
                e.Handled = true;
                break;

            case Key.Escape:
                if (SuggestionPopup.IsOpen)
                {
                    ClosePopup();
                }
                else
                {
                    Cancelled?.Invoke(this, EventArgs.Empty);
                }

                e.Handled = true;
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        var count = SuggestionList.Items.Count;
        if (count == 0) return;

        SuggestionList.SelectedIndex = (SuggestionList.SelectedIndex + delta + count) % count;
        SuggestionList.ScrollIntoView(SuggestionList.SelectedItem);
    }

    private void SuggestionList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Validation dès le clic, intercepté avant que l'élément ne prenne le focus :
        // la zone de saisie le garde. Un clic hors élément (barre de défilement)
        // suit son cours normal.
        if (e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(SuggestionList, source) is ListBoxItem item)
        {
            Submit(item.DataContext as LanguageSuggestion);
            e.Handled = true;
        }
    }

    private void QueryTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => ClosePopup();

    // Suggestion choisie ou, à défaut, la saisie : nom canonique si elle désigne une
    // langue connue, texte libre sinon (le modèle comprend « Brazilian Portuguese »).
    private void Submit(LanguageSuggestion? suggestion)
    {
        var typed = QueryTextBox.Text.Trim();
        var language = suggestion?.EnglishName ?? LanguageCatalog.FindExact(typed) ?? typed;
        if (language.Length == 0) return;

        Reset();
        LanguageSubmitted?.Invoke(this, language);
    }

    private void ClosePopup() => SuggestionPopup.IsOpen = false;
}
