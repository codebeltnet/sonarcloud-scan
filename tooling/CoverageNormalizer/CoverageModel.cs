namespace CoverageNormalizer;

/// <summary>A single execution observation of a source line.</summary>
public readonly record struct LineObservation(string File, int Line, bool Covered);

/// <summary>
/// A single execution observation of one logical branch outcome.
/// Identity is (File, Method, Ordinal, Path): the branch outcome's position in the
/// method's branch sequence. IL offsets are deliberately not part of identity because
/// they are not stable across builds. The observed source line can differ between
/// builds (line-attribution drift) and is therefore not part of identity either.
/// </summary>
public readonly record struct BranchObservation(string File, string Method, int Ordinal, int Path, int Line, bool Covered);

/// <summary>
/// Deterministic logical coverage model:
/// execution evidence is additive (union), source structure is not (each logical
/// branch contributes exactly once to the denominator).
/// </summary>
public sealed class CoverageModel
{
    private sealed class LineState
    {
        public bool Covered;
    }

    private sealed class BranchState
    {
        public bool Covered;
        public int MinLine;
        public int MaxLine;
        public int Observations;
    }

    private readonly Dictionary<(string File, int Line), LineState> _lines = new();
    private readonly Dictionary<(string File, string Method, int Ordinal, int Path), BranchState> _branches = new();

    /// <summary>Total branch observations consumed (for dedup statistics).</summary>
    public long BranchObservations { get; private set; }

    /// <summary>Total line observations consumed (for dedup statistics).</summary>
    public long LineObservations { get; private set; }

    public void Add(in LineObservation observation)
    {
        LineObservations++;
        var key = (observation.File, observation.Line);
        if (!_lines.TryGetValue(key, out var state))
        {
            _lines[key] = new LineState { Covered = observation.Covered };
        }
        else if (observation.Covered)
        {
            state.Covered = true; // union: covered if ANY execution covers the line
        }
    }

    public void Add(in BranchObservation observation)
    {
        BranchObservations++;
        var key = (observation.File, observation.Method, observation.Ordinal, observation.Path);
        if (!_branches.TryGetValue(key, out var state))
        {
            _branches[key] = new BranchState
            {
                Covered = observation.Covered,
                MinLine = observation.Line,
                MaxLine = observation.Line,
                Observations = 1,
            };
        }
        else
        {
            if (observation.Covered)
            {
                state.Covered = true; // union: covered if ANY execution covers the outcome
            }

            if (observation.Line < state.MinLine)
            {
                state.MinLine = observation.Line; // deterministic line attribution
            }

            if (observation.Line > state.MaxLine)
            {
                state.MaxLine = observation.Line;
            }

            state.Observations++;
        }
    }

    public sealed record MergedLine(int Line, bool Covered);

    public sealed record MergedBranch(int Line, int Ordinal, int Path, bool Covered);

    /// <summary>The exact line model persisted in Sonar generic coverage.</summary>
    public sealed record SonarLine(int Line, bool Covered, int BranchesToCover, int CoveredBranches, bool HasSequencePoint);

    /// <summary>
    /// Sequence-point lines union attributed branch lines. Branch-only lines must
    /// be emitted because generic coverage attaches branches to lineToCover.
    /// Sequence evidence determines line coverage when present; otherwise any
    /// covered branch determines coverage of the branch-only line.
    /// </summary>
    public IReadOnlyList<SonarLine> SonarLinesOf(string file)
    {
        var lines = LinesOf(file).ToDictionary(l => l.Line);
        var branches = BranchesOf(file).GroupBy(b => b.Line)
            .ToDictionary(g => g.Key, g => (Total: g.Count(), Covered: g.Count(b => b.Covered)));
        return lines.Keys.Union(branches.Keys).OrderBy(l => l).Select(number =>
        {
            var hasSequence = lines.TryGetValue(number, out var line);
            branches.TryGetValue(number, out var outcomes);
            return new SonarLine(number, hasSequence ? line!.Covered : outcomes.Covered > 0,
                outcomes.Total, outcomes.Covered, hasSequence);
        }).ToArray();
    }

    /// <summary>Counts the emitted lineToCover model, optionally for one file.</summary>
    public (long Covered, long Total) SonarLineTotals(string? file = null)
    {
        var lines = (file is null ? Files : new[] { file }).SelectMany(SonarLinesOf).ToArray();
        return (lines.LongCount(l => l.Covered), lines.LongLength);
    }

    public sealed record BranchDetail(string File, string Method, int Ordinal, int Path, int MinLine, int MaxLine, int Observations, bool Covered);

    public IReadOnlyList<string> Files => _lines.Keys.Select(k => k.File)
        .Concat(_branches.Keys.Select(k => k.File))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(f => f, StringComparer.Ordinal)
        .ToArray();

    public IReadOnlyList<MergedLine> LinesOf(string file)
    {
        return _lines.Where(kv => kv.Key.File == file)
            .Select(kv => new MergedLine(kv.Key.Line, kv.Value.Covered))
            .OrderBy(l => l.Line)
            .ToArray();
    }

    public IReadOnlyList<MergedBranch> BranchesOf(string file)
    {
        return _branches.Where(kv => kv.Key.File == file)
            .Select(kv => new MergedBranch(kv.Value.MinLine, kv.Key.Ordinal, kv.Key.Path, kv.Value.Covered))
            .OrderBy(b => b.Line).ThenBy(b => b.Ordinal).ThenBy(b => b.Path)
            .ToArray();
    }

    /// <summary>
    /// Per-identity detail including observed line spread (MaxLine - MinLine).
    /// A large spread on an identity signals either line-attribution drift or a
    /// potential mid-sequence decision shift between compiler variants.
    /// </summary>
    public IReadOnlyList<BranchDetail> BranchDetails()
    {
        return _branches
            .Select(kv => new BranchDetail(kv.Key.File, kv.Key.Method, kv.Key.Ordinal, kv.Key.Path, kv.Value.MinLine, kv.Value.MaxLine, kv.Value.Observations, kv.Value.Covered))
            .OrderBy(d => d.File, StringComparer.Ordinal)
            .ThenBy(d => d.Method, StringComparer.Ordinal)
            .ThenBy(d => d.Ordinal)
            .ThenBy(d => d.Path)
            .ToArray();
    }

    public (long Covered, long Total) SequenceLineTotals()
    {
        long covered = 0;
        foreach (var state in _lines.Values)
        {
            if (state.Covered) { covered++; }
        }

        return (covered, _lines.Count);
    }

    public (long Covered, long Total) BranchTotals()
    {
        long covered = 0;
        foreach (var state in _branches.Values)
        {
            if (state.Covered) { covered++; }
        }

        return (covered, _branches.Count);
    }

    public (long Covered, long Total) SequenceLineTotals(string file)
    {
        long covered = 0;
        long total = 0;
        foreach (var kv in _lines)
        {
            if (kv.Key.File != file) { continue; }

            total++;
            if (kv.Value.Covered) { covered++; }
        }

        return (covered, total);
    }

    public (long Covered, long Total) BranchTotals(string file)
    {
        long covered = 0;
        long total = 0;
        foreach (var kv in _branches)
        {
            if (kv.Key.File != file) { continue; }

            total++;
            if (kv.Value.Covered) { covered++; }
        }

        return (covered, total);
    }
}
