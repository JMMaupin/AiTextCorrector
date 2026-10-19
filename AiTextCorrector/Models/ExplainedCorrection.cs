using System.Collections.Generic;

namespace AiTextCorrector.Models;

/// <summary>Une faute relevée par le modèle, avec la règle qui la corrige.</summary>
public sealed record ErrorExplanation(string Original, string Correction, string Category, string Rule, string Explanation);

/// <summary>Correction stricte accompagnée de l'explication de chaque faute (option « Explain »).</summary>
public sealed record ExplainedCorrection(string Corrected, IReadOnlyList<ErrorExplanation> Errors);
