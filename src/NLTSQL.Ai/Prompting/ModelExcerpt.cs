using System.Globalization;
using System.Text;
using NLTSQL.Semantics.Model;

namespace NLTSQL.Ai.Prompting;

/// <summary>
/// Renders a semantic model as the context a language model plans against.
/// </summary>
/// <remarks>
/// <para>
/// This text is the single largest lever on answer quality. A measure the excerpt does not mention
/// cannot be chosen; a dimension whose permitted values are listed turns "cancelled orders" into
/// the right filter instead of a guess at the encoding.
/// </para>
/// <para>
/// The prototype renders the whole model. That is the right call while models stay small: it is
/// exact, has no retrieval to tune, and cannot omit the one entity the question needed. It stops
/// being the right call when the excerpt no longer fits comfortably in the context window, which
/// is the point at which the retrieval step from the full design earns its complexity.
/// </para>
/// <para>
/// Hidden elements are omitted entirely. They exist so measures can name their columns, and
/// showing them would invite the model to group by a raw amount column.
/// </para>
/// </remarks>
public static class ModelExcerpt
{
    /// <summary>Values beyond this many are truncated; a long list stops informing and starts crowding.</summary>
    private const int MaxListedValues = 25;

    /// <summary>Renders <paramref name="model"/> as prompt context.</summary>
    public static string Render(SemanticModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"# Datenmodell \"{model.Label ?? model.Name}\"\n");

        if (!string.IsNullOrWhiteSpace(model.Description))
        {
            text.Append(CultureInfo.InvariantCulture, $"{model.Description}\n");
        }

        foreach (var entity in model.Entities.Where(e => !e.Hidden))
        {
            RenderEntity(text, entity);
        }

        RenderGlossary(text, model);

        return text.ToString();
    }

    private static void RenderEntity(StringBuilder text, Entity entity)
    {
        text.Append(CultureInfo.InvariantCulture, $"\n## Entitaet: {entity.Name}\n");
        text.Append(CultureInfo.InvariantCulture, $"Bezeichnung: {entity.Label}\n");

        if (!string.IsNullOrWhiteSpace(entity.Description))
        {
            text.Append(CultureInfo.InvariantCulture, $"Bedeutung: {entity.Description}\n");
        }

        AppendSynonyms(text, entity.Synonyms);

        var measures = entity.Measures.Where(m => !m.Hidden).ToList();
        var metrics = entity.Metrics.Where(m => !m.Hidden).ToList();

        if (measures.Count > 0 || metrics.Count > 0)
        {
            text.Append("\nKennzahlen (fuer `measures`):\n");
            foreach (var measure in measures)
            {
                AppendElement(text, measure.Name, measure.Label, measure.Description, measure.Synonyms);
            }

            foreach (var metric in metrics)
            {
                AppendElement(text, metric.Name, metric.Label, metric.Description, metric.Synonyms);
            }
        }

        var dimensions = entity.Dimensions.Where(d => !d.Hidden).ToList();
        if (dimensions.Count > 0)
        {
            text.Append("\nMerkmale (fuer `group_by` und `filters`):\n");
            foreach (var dimension in dimensions)
            {
                AppendElement(
                    text,
                    dimension.Name,
                    dimension.Label,
                    dimension.Description,
                    dimension.Synonyms,
                    $"Typ {dimension.DataType.ToString().ToLowerInvariant()}");

                if (dimension.Values.Count > 0)
                {
                    // The exact permitted values matter more than anything else here: they are what
                    // stop the model inventing a status spelling the database has never seen.
                    var listed = dimension.Values.Take(MaxListedValues);
                    var suffix = dimension.Values.Count > MaxListedValues ? ", …" : string.Empty;
                    text.Append(CultureInfo.InvariantCulture, $"    moegliche Werte: {string.Join(", ", listed)}{suffix}\n");
                }
            }
        }

        var timeDimensions = entity.TimeDimensions.Where(d => !d.Hidden).ToList();
        if (timeDimensions.Count > 0)
        {
            text.Append("\nZeitmerkmale (fuer `group_by` mit `grain` und fuer Zeitfilter):\n");
            foreach (var time in timeDimensions)
            {
                AppendElement(
                    text,
                    time.Name,
                    time.Label,
                    time.Description,
                    time.Synonyms,
                    "Stufen: " + string.Join(", ", time.Granularities.Select(g => g.ToString().ToLowerInvariant())));
            }
        }
    }

    private static void RenderGlossary(StringBuilder text, SemanticModel model)
    {
        if (model.Glossary.Count == 0)
        {
            return;
        }

        text.Append("\n## Fachbegriffe\n");
        foreach (var term in model.Glossary)
        {
            text.Append(CultureInfo.InvariantCulture, $"- {term.Term}: {term.Definition}\n");
        }
    }

    private static void AppendElement(
        StringBuilder text,
        string name,
        string label,
        string? description,
        IReadOnlyList<string> synonyms,
        string? extra = null)
    {
        text.Append(CultureInfo.InvariantCulture, $"- {name} — {label}");

        if (extra is not null)
        {
            text.Append(CultureInfo.InvariantCulture, $" ({extra})");
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            text.Append(CultureInfo.InvariantCulture, $". {description}");
        }

        if (synonyms.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $" Auch genannt: {string.Join(", ", synonyms)}.");
        }

        text.Append('\n');
    }

    private static void AppendSynonyms(StringBuilder text, IReadOnlyList<string> synonyms)
    {
        if (synonyms.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"Auch genannt: {string.Join(", ", synonyms)}\n");
        }
    }
}
