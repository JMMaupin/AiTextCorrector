using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using AiTextCorrector.Localization;
using AiTextCorrector.Models;
using AiTextCorrector.Services;

namespace AiTextCorrector.Controls;

/// <summary>
/// Texte annoté « au stylo » : chaque faute en rouge, suivie de sa correction en
/// vert, avec une info-bulle qui rappelle la règle. Lecture seule ; un clic
/// demande le retour à l'édition (<see cref="EditRequested"/>).
/// </summary>
public partial class AnnotatedTextView : UserControl
{
    // Faute en rouge, correction en vert (le vert « disponible » de l'onglet
    // Shortcut) : couleurs lisibles sur les deux thèmes, clair et sombre.
    private static readonly Brush ErrorBrush = Frozen(Color.FromRgb(0xE5, 0x48, 0x4D));
    private static readonly Brush CorrectionBrush = Frozen(Color.FromRgb(0x22, 0xC5, 0x5E));

    // Surlignage neutre au survol : ni rouge ni vert, pour ne pas brouiller le code couleur.
    private static readonly Brush HoverBrush = Frozen(Color.FromArgb(0x33, 0x80, 0x80, 0x80));

    public AnnotatedTextView()
    {
        InitializeComponent();
    }

    public event EventHandler? EditRequested;

    /// <summary>Affiche le texte annoté et renvoie le nombre de corrections signalées.</summary>
    public int Show(IReadOnlyList<DiffSegment> segments, IReadOnlyList<ErrorExplanation> errors)
    {
        AnnotatedText.Inlines.Clear();
        var matcher = new ExplanationMatcher(errors);
        var count = 0;

        foreach (var segment in segments)
        {
            if (segment.IsChange)
            {
                AnnotatedText.Inlines.Add(BuildCorrection(segment, matcher.Find(segment)));
                count++;
            }
            else
            {
                AnnotatedText.Inlines.Add(new Run(segment.Original));
            }
        }

        return count;
    }

    private static Span BuildCorrection(DiffSegment change, ErrorExplanation? explanation)
    {
        var span = new Span { Cursor = Cursors.Help };
        var before = change.Original;
        var after = change.Corrected;

        if (before.Length > 0)
        {
            span.Inlines.Add(new Run(before) { Foreground = ErrorBrush });
        }

        // Un espace entre la faute et sa correction, sans quoi « les » et « le » se
        // liraient « lesle ».
        if (before.Length > 0 && after.Length > 0 && !char.IsWhiteSpace(before[^1]) && !char.IsWhiteSpace(after[0]))
        {
            span.Inlines.Add(new Run(" "));
        }

        if (after.Length > 0)
        {
            span.Inlines.Add(new Run(after) { Foreground = CorrectionBrush, FontWeight = FontWeights.SemiBold });
        }

        span.ToolTip = BuildToolTip(change, explanation);
        ToolTipService.SetInitialShowDelay(span, 150);
        ToolTipService.SetShowDuration(span, int.MaxValue);
        span.MouseEnter += (_, _) => span.Background = HoverBrush;
        span.MouseLeave += (_, _) => span.Background = null;
        return span;
    }

    private static object BuildToolTip(DiffSegment change, ErrorExplanation? explanation)
    {
        var panel = new StackPanel { MaxWidth = 380 };
        panel.Children.Add(new TextBlock
        {
            Text = Describe(change),
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        if (explanation is null)
        {
            panel.Children.Add(new TextBlock
            {
                Text = Loc.Text("Annotated.NoExplanation"),
                Opacity = 0.75,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            return panel;
        }

        var heading = string.Join(" · ", new[] { explanation.Category, explanation.Rule }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part.Trim()));
        if (heading.Length > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = heading,
                Opacity = 0.75,
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
        }

        if (!string.IsNullOrWhiteSpace(explanation.Explanation))
        {
            panel.Children.Add(new TextBlock
            {
                Text = explanation.Explanation.Trim(),
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
        }

        return panel;
    }

    private static string Describe(DiffSegment change)
    {
        var before = change.Original.Trim();
        var after = change.Corrected.Trim();
        if (before.Length == 0) return $"+ {after}";
        if (after.Length == 0) return $"{before} → {Loc.Text("Annotated.Deleted")}";
        return $"{before} → {after}";
    }

    private void Frame_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Un clic dans la barre de défilement sert à défiler, pas à quitter la lecture.
        for (var node = e.OriginalSource as DependencyObject; node is not null; node = ParentOf(node))
        {
            if (node is ScrollBar) return;
        }

        EditRequested?.Invoke(this, EventArgs.Empty);
    }

    // Les éléments de texte (Run, Span) ne sont pas des Visual : on remonte alors
    // par l'arbre logique.
    private static DependencyObject? ParentOf(DependencyObject node) =>
        node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
