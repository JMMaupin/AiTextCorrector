using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;

namespace AiTextCorrector.Controls;

/// <summary>
/// Annulation caractère par caractère pour une TextBox : chaque modification —
/// une lettre tapée, un effacement, un collage, un remplacement par le code
/// (injection du presse-papiers, « Vider ») — est une étape de Ctrl+Z.
///
/// L'annulation native de WPF regroupe la frappe en blocs (un mot, voire une
/// phrase) : impossible d'y revenir d'une seule lettre.
/// Ctrl+Y ou Ctrl+Maj+Z rétablissent.
/// </summary>
public sealed class CharacterUndoHistory
{
    // Au-delà, les étapes les plus anciennes sont oubliées : l'application vit des
    // jours dans la zone de notification, l'historique ne doit pas croître sans fin.
    private const int MaxSteps = 10_000;

    private readonly TextBox _textBox;
    private readonly LinkedList<TextEdit> _undo = new();
    private readonly Stack<TextEdit> _redo = new();
    private string _previousText;
    private bool _isApplying;

    private CharacterUndoHistory(TextBox textBox)
    {
        _textBox = textBox;
        _previousText = textBox.Text;

        // Historique natif coupé : il enregistrerait en double, avec sa propre granularité.
        textBox.IsUndoEnabled = false;
        textBox.TextChanged += OnTextChanged;

        textBox.CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo,
            (_, e) => { Undo(); e.Handled = true; },
            (_, e) => { e.CanExecute = _undo.Count > 0; e.Handled = true; }));
        textBox.CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo,
            (_, e) => { Redo(); e.Handled = true; },
            (_, e) => { e.CanExecute = _redo.Count > 0; e.Handled = true; }));

        // Ctrl+Y est le raccourci WPF de Redo ; Ctrl+Maj+Z celui de nombreux éditeurs.
        textBox.InputBindings.Add(new KeyBinding(ApplicationCommands.Redo, Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
    }

    /// <summary>Branche l'historique sur la TextBox, qui le garde en vie via ses gestionnaires.</summary>
    public static void Attach(TextBox textBox) => _ = new CharacterUndoHistory(textBox);

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        var text = _textBox.Text;

        if (!_isApplying && ToEdit(_previousText, text, e.Changes) is { } edit)
        {
            _undo.AddLast(edit);
            if (_undo.Count > MaxSteps)
            {
                _undo.RemoveFirst();
            }

            // Une saisie nouvelle invalide ce qui avait été annulé.
            _redo.Clear();
        }

        _previousText = text;
    }

    private void Undo()
    {
        if (_undo.Last is not { } last) return;

        _undo.RemoveLast();
        var edit = last.Value;
        Apply(edit.Offset, edit.Added.Length, edit.Removed);
        _redo.Push(edit);
    }

    private void Redo()
    {
        if (!_redo.TryPop(out var edit)) return;

        Apply(edit.Offset, edit.Removed.Length, edit.Added);
        _undo.AddLast(edit);
    }

    // Remplace seulement la plage concernée, sans réécrire tout le texte (le
    // défilement est préservé), puis place le curseur juste après.
    private void Apply(int offset, int length, string replacement)
    {
        _isApplying = true;
        try
        {
            _textBox.Select(offset, length);
            _textBox.SelectedText = replacement;
            _textBox.Select(offset + replacement.Length, 0);
        }
        finally
        {
            _isApplying = false;
        }
    }

    // Modification telle que WPF la décrit, si elle est unique et vérifiée ; sinon
    // déduite par comparaison (préfixe et suffixe communs). La description WPF est
    // préférée car exacte : dans « aaa », la comparaison ne sait pas QUEL « a » a
    // été tapé, et le curseur se replacerait au mauvais endroit.
    private static TextEdit? ToEdit(string before, string after, ICollection<TextChange> changes)
    {
        if (string.Equals(before, after, StringComparison.Ordinal)) return null;

        if (changes.Count == 1)
        {
            var c = changes.First();
            var tailLength = before.Length - c.Offset - c.RemovedLength;
            if (c.Offset >= 0 && tailLength >= 0 &&
                after.Length == c.Offset + c.AddedLength + tailLength &&
                string.CompareOrdinal(before, 0, after, 0, c.Offset) == 0 &&
                string.CompareOrdinal(before, c.Offset + c.RemovedLength, after, c.Offset + c.AddedLength, tailLength) == 0)
            {
                return new TextEdit(c.Offset, before.Substring(c.Offset, c.RemovedLength), after.Substring(c.Offset, c.AddedLength));
            }
        }

        var max = Math.Min(before.Length, after.Length);
        var prefix = 0;
        while (prefix < max && before[prefix] == after[prefix]) prefix++;

        var suffix = 0;
        while (suffix < max - prefix && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix]) suffix++;

        return new TextEdit(prefix,
            before.Substring(prefix, before.Length - prefix - suffix),
            after.Substring(prefix, after.Length - prefix - suffix));
    }

    private sealed record TextEdit(int Offset, string Removed, string Added);
}
