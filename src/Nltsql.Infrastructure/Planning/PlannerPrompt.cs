using System.Globalization;
using System.Text;
using Nltsql.Core.Semantics;

namespace Nltsql.Infrastructure.Planning;

/// <summary>
/// Builds the planner's system prompt from the live semantic model.
/// </summary>
/// <remarks>
/// The catalogue is generated, never hand-maintained: when the customer
/// adds a measure to the Cube model, the planner can use it on the next
/// request without a code change. Descriptions and synonyms from the
/// model are what carry the domain vocabulary — this is the whole reason
/// the semantic layer sits between the question and the warehouse.
/// </remarks>
internal static class PlannerPrompt
{
    public static string BuildSystemPrompt(SemanticModel model, string? domainBriefing, DateOnly today)
    {
        var prompt = new StringBuilder();

        prompt.AppendLine("""
            Du übersetzt fachliche Fragen in strukturierte Abfragen auf einem semantischen Datenmodell.

            Regeln:
            - Verwende ausschließlich die unten katalogisierten Datenbereiche, Kennzahlen und Merkmale.
              Erfinde niemals Namen und leite keine Namen aus den Beschreibungen ab.
            - Verwende die technischen Namen (z. B. `oee`), nicht die Anzeigetitel.
            - Alle Felder einer Abfrage müssen aus genau einem Datenbereich stammen.
            - Kennzahlen sind bereits aggregiert. Bilde keine Summen oder Mittelwerte selbst
              und kombiniere keine Kennzahlen zu neuen Formeln.
            - Setze `granularity` nur, wenn der Verlauf über die Zeit gefragt ist. Für eine Frage
              nach einem Zeitraum ohne Verlauf genügt der Zeitraum ohne Granularität.
            - Wähle einen Zeitraum nur, wenn die Frage einen nennt oder klar impliziert.
            - Sortiere absteigend nach der wichtigsten Kennzahl, wenn nach "Top", "größte",
              "schlechteste" o. ä. gefragt wird, und setze ein passendes `limit`.
            - Wenn die Frage mit dem Modell nicht beantwortbar ist, setze `answerable` auf false
              und begründe kurz auf Deutsch, welche Angabe fehlt. Rate nicht.
            - `interpretation` ist ein Satz auf Deutsch, der beschreibt, was die Abfrage berechnet.
              Der Nutzer prüft daran, ob er richtig verstanden wurde.
            """);

        prompt.AppendLine();
        prompt.Append("Heutiges Datum: ").AppendLine(today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        if (!string.IsNullOrWhiteSpace(domainBriefing))
        {
            prompt.AppendLine();
            prompt.AppendLine("Fachliche Hinweise:");
            prompt.AppendLine(domainBriefing.Trim());
        }

        prompt.AppendLine();
        prompt.AppendLine("# Katalog");

        foreach (var view in model.Views)
        {
            AppendView(prompt, view);
        }

        return prompt.ToString();
    }

    private static void AppendView(StringBuilder prompt, SemanticView view)
    {
        prompt.AppendLine();
        prompt.Append("## Datenbereich `").Append(view.Name).Append("` — ").AppendLine(view.Title);

        if (!string.IsNullOrWhiteSpace(view.Description))
        {
            prompt.AppendLine(Collapse(view.Description));
        }

        if (view.Measures.Count > 0)
        {
            prompt.AppendLine();
            prompt.AppendLine("Kennzahlen:");

            foreach (var measure in view.Measures)
            {
                AppendMember(prompt, measure, measure.Format is null ? null : $"Format: {measure.Format}");
            }
        }

        if (view.Dimensions.Count > 0)
        {
            prompt.AppendLine();
            prompt.AppendLine("Merkmale:");

            foreach (var dimension in view.Dimensions)
            {
                AppendMember(prompt, dimension, $"Typ: {dimension.Type.ToString().ToLowerInvariant()}");
            }
        }
    }

    private static void AppendMember(StringBuilder prompt, SemanticMember member, string? suffix)
    {
        prompt.Append("- `").Append(member.Name).Append("` (").Append(member.Title).Append(')');

        if (!string.IsNullOrWhiteSpace(member.Description))
        {
            prompt.Append(": ").Append(Collapse(member.Description));
        }

        if (member.Synonyms.Count > 0)
        {
            prompt.Append(" [auch: ").AppendJoin(", ", member.Synonyms).Append(']');
        }

        if (!string.IsNullOrWhiteSpace(suffix))
        {
            prompt.Append(" {").Append(suffix).Append('}');
        }

        prompt.AppendLine();
    }

    /// <summary>Flattens the wrapped YAML descriptions onto one line.</summary>
    private static string Collapse(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
