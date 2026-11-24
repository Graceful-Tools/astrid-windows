using System.Text.RegularExpressions;
using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// A decision being written in task details: the question, its options and, if wanted, a
/// trade-off grid rated in traffic-light colours (task <c>b0f521c6</c>).
/// </summary>
/// <remarks>
/// <para>
/// A port of astrid-ios <c>DecisionDraft.swift</c> (ADEV-189, ADEV-231), which is the
/// specification, and its tests are ported case for case in <c>DecisionDraftTests</c>. It becomes
/// ordinary markdown in the format astrid-web <c>docs/DECISIONS.md</c> describes, posted as a
/// comment or added to the description, so every app draws it as choices and a coloured grid.
/// Nothing about a decision is kept anywhere but that text.
/// </para>
/// <para>
/// Which tokens are ratings is the core's (<see cref="AstridClient.RatingKey"/>); what the grid
/// looks like on screen is the core's renderer, asked through
/// <see cref="AstridClient.RenderMarkdown"/>. What is left here is turning a form into text, which
/// is the composer's job on every client.
/// </para>
/// </remarks>
public sealed partial class DecisionDraft
{
    /// <summary>One choice. Identified by an id, so two options with the same text stay apart.</summary>
    public sealed class Option
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Text { get; set; } = string.Empty;
    }

    /// <summary>One column of the grid.</summary>
    public sealed class Criterion
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;

        /// <summary>What it means, optional (ADEV-231): one line of the key under the grid.</summary>
        public string Detail { get; set; } = string.Empty;
    }

    /// <summary>One option against one criterion: a light, or none, and an optional note.</summary>
    public sealed record Cell(string? Rating, string Text);

    /// <summary>One of the lights a cell offers.</summary>
    /// <param name="Token">What is written in the grid: <c>--</c> … <c>++</c>.</param>
    /// <param name="Rating"><c>veryPoor</c> … <c>veryGood</c>, which names its colour and its word.</param>
    public sealed record Light(string Token, string Rating);

    public enum Problem
    {
        NoQuestion,
        TooFewOptions,

        /// <summary>A note with no colour: the table would no longer read as a grid.</summary>
        ReasonWithoutRating,
    }

    /// <summary>The grid's columns, as web's guide asks: three to five; one or two are allowed.</summary>
    public const int MaxCriteria = 5;

    private readonly Dictionary<(Guid Option, Guid Criterion), Cell> _cells = [];

    public string Question { get; set; } = string.Empty;

    public List<Option> Options { get; } = [new(), new()];

    /// <summary>The option marked <c>(recommended)</c>, if any.</summary>
    public Guid? Recommended { get; set; }

    public List<Criterion> Criteria { get; } = [];

    public bool CanAddCriterion => Criteria.Count < MaxCriteria;

    /// <summary>The five plain tokens, worst to best, from the core's key.</summary>
    /// <remarks>
    /// The emoji forms are read and kept as written, but not offered — the same choice iOS makes.
    /// The fallback is only for a core that cannot answer, so the composer still has its lights.
    /// </remarks>
    public static IReadOnlyList<Light> Lights()
    {
        var key = AstridClient.RatingKey()
            .Where(e => e.Token.All(char.IsAscii))
            .OrderBy(e => e.Rank)
            .Select(e => new Light(e.Token, e.LabelKey))
            .ToList();
        return key.Count > 0
            ? key
            : [new("--", "veryPoor"), new("-", "poor"), new("~", "mixed"), new("+", "good"), new("++", "veryGood")];
    }

    public Cell CellAt(Guid option, Guid criterion) =>
        _cells.TryGetValue((option, criterion), out var cell) ? cell : new Cell(null, string.Empty);

    public void Set(Cell cell, Guid option, Guid criterion) => _cells[(option, criterion)] = cell;

    /// <summary>Light a cell, or clear it when that light is already lit. The note stays.</summary>
    public void Toggle(string token, Guid option, Guid criterion)
    {
        var cell = CellAt(option, criterion);
        Set(cell with { Rating = cell.Rating == token ? null : token }, option, criterion);
    }

    public void SetNote(string text, Guid option, Guid criterion) =>
        Set(CellAt(option, criterion) with { Text = text }, option, criterion);

    public void AddOption() => Options.Add(new Option());

    /// <summary>Drop an option; two always stay, because a decision needs two.</summary>
    public void RemoveOption(Guid id)
    {
        if (Options.Count <= 2)
        {
            return;
        }
        Options.RemoveAll(o => o.Id == id);
        if (Recommended == id)
        {
            Recommended = null;
        }
    }

    /// <summary>Star an option, or un-star it when it is already the one.</summary>
    public void ToggleRecommended(Guid id) => Recommended = Recommended == id ? null : id;

    public void AddCriterion()
    {
        if (CanAddCriterion)
        {
            Criteria.Add(new Criterion());
        }
    }

    public void RemoveCriterion(Guid id) => Criteria.RemoveAll(c => c.Id == id);

    // ── Writing ───────────────────────────────────────────────────────────────────────────────

    private List<Option> NamedOptions => Options.Where(o => Line(o.Text).Length > 0).ToList();

    private List<Criterion> NamedCriteria => Criteria.Where(c => Line(c.Name).Length > 0).ToList();

    /// <summary>Only the cells of options and criteria that are written down.</summary>
    private IEnumerable<Cell> KeptCells =>
        NamedOptions.SelectMany(o => NamedCriteria.Select(c => CellAt(o.Id, c.Id)));

    /// <summary>What stops it being posted, or null when nothing does.</summary>
    public Problem? CurrentProblem
    {
        get
        {
            if (Line(Question).Length == 0)
            {
                return Problem.NoQuestion;
            }
            if (NamedOptions.Count < 2)
            {
                return Problem.TooFewOptions;
            }
            if (KeptCells.Any(c => c.Rating is null && Line(c.Text).Length > 0))
            {
                return Problem.ReasonWithoutRating;
            }
            return null;
        }
    }

    /// <summary>A grid only once a cell has a colour; criteria alone add nothing.</summary>
    private bool HasGrid => NamedCriteria.Count > 0 && KeptCells.Any(c => c.Rating is not null);

    /// <summary>The decision as <c>docs/DECISIONS.md</c> writes it, byte for byte as iOS does.</summary>
    public string Markdown
    {
        get
        {
            var lines = new List<string> { $"**Decision:** {Line(Question)}", string.Empty };
            var options = NamedOptions;
            for (var i = 0; i < options.Count; i++)
            {
                var mark = options[i].Id == Recommended ? " (recommended)" : string.Empty;
                lines.Add($"- **{i + 1}.** {Line(options[i].Text)}{mark}");
            }
            if (HasGrid)
            {
                lines.Add(string.Empty);
                lines.Add(GridMarkdown(options, NamedCriteria));
                var key = NamedCriteria.Where(c => Line(c.Detail).Length > 0)
                    .Select(c => $"- **{Line(c.Name)}:** {Line(c.Detail)}")
                    .ToList();
                if (key.Count > 0)
                {
                    lines.Add(string.Empty);
                    lines.AddRange(key);
                }
            }
            return string.Join("\n", lines);
        }
    }

    /// <summary><paramref name="description"/> with the decision after what is already there.</summary>
    public string AppendedTo(string description) =>
        description.Trim().Length == 0
            ? Markdown
            : TrailingSpace().Replace(description, string.Empty) + "\n\n" + Markdown;

    /// <summary>
    /// A header, a separator, then one numbered row per option. No score column, ever (web
    /// ADEV-178): the person decides. astrid-ios <c>DecisionGrid.markdown</c>.
    /// </summary>
    private string GridMarkdown(List<Option> options, List<Criterion> criteria)
    {
        var header = new List<string> { "Option" };
        header.AddRange(criteria.Select(c => Line(c.Name)));
        var lines = new List<string>
        {
            "| " + string.Join(" | ", header.Select(Escape)) + " |",
            "|" + string.Join("|", header.Select(_ => "---")) + "|",
        };
        for (var i = 0; i < options.Count; i++)
        {
            var row = new List<string> { $"{i + 1}. {Line(options[i].Text)}" };
            foreach (var criterion in criteria)
            {
                var cell = CellAt(options[i].Id, criterion.Id);
                row.Add(string.Join(" ",
                    new[] { cell.Rating, Line(cell.Text) }.Where(p => !string.IsNullOrEmpty(p))));
            }
            lines.Add("| " + string.Join(" | ", row.Select(Escape)) + " |");
        }
        return string.Join("\n", lines);
    }

    private static string Escape(string text) =>
        text.Replace("\r\n", " ").Replace("\n", " ").Replace("\\", "\\\\").Replace("|", "\\|").Trim();

    private static readonly char[] Newlines = ['\n', '\u000B', '\u000C', '\r', '\u0085', LineSeparator, ParagraphSeparator];

    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;

    /// <summary>One line: a decision's question, options and cells never span lines.</summary>
    private static string Line(string text) =>
        string.Join(" ", text.Split(Newlines).Select(part => part.Trim()).Where(part => part.Length > 0));

    [GeneratedRegex(@"\s+$")]
    private static partial Regex TrailingSpace();
}
