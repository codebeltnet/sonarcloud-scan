namespace CoverageNormalizer;

/// <summary>
/// Partition-aware structural diagnostics used to validate the logical branch
/// identity strategy (File, Method, Ordinal, Path) across build variants.
/// A "partition" is one coverage dimension label (e.g. Debug-Linux-X64+net10.0)
/// derived by the caller from each input report's location. The diagnostics do
/// not affect the emitted coverage report; they produce the evidence that
/// equivalent branch points deduplicate and that compiler-variant divergence is
/// detected rather than silently collapsed.
/// </summary>
public sealed class Diagnostics
{
    // (File, Method) -> partition -> exact logical (ordinal, path) structure
    private readonly Dictionary<(string File, string Method), Dictionary<string, SortedSet<(int Ordinal, int Path)>>> _structures = new();
    private readonly SortedSet<string> _partitions = new(StringComparer.Ordinal);

    /// <summary>Records a report partition even if it contains no branch points.</summary>
    public void RecordPartition(string partition) => _partitions.Add(partition);

    // (File, Method, Ordinal, Path) -> partition -> set of source lines observed
    private readonly Dictionary<(string File, string Method, int Ordinal, int Path), Dictionary<string, SortedSet<int>>> _lines = new();

    public void Record(string partition, in BranchObservation observation)
    {
        RecordPartition(partition);
        var methodKey = (observation.File, observation.Method);
        if (!_structures.TryGetValue(methodKey, out var byPartition))
        {
            _structures[methodKey] = byPartition = new Dictionary<string, SortedSet<(int Ordinal, int Path)>>(StringComparer.Ordinal);
        }

        if (!byPartition.TryGetValue(partition, out var pairs))
        {
            byPartition[partition] = pairs = new SortedSet<(int Ordinal, int Path)>();
        }

        pairs.Add((observation.Ordinal, observation.Path));

        var idKey = (observation.File, observation.Method, observation.Ordinal, observation.Path);
        if (!_lines.TryGetValue(idKey, out var linesByPartition))
        {
            _lines[idKey] = linesByPartition = new Dictionary<string, SortedSet<int>>(StringComparer.Ordinal);
        }

        if (!linesByPartition.TryGetValue(partition, out var lines))
        {
            linesByPartition[partition] = lines = new SortedSet<int>();
        }

        lines.Add(observation.Line);
    }

    /// <summary>An ordered, inspectable structural signature, not just a pair count.</summary>
    public sealed record PartitionStructure(string Partition, IReadOnlyList<(int Ordinal, int Path)> Pairs);

    public sealed record MethodStructure(
        string File,
        string Method,
        bool Divergent,
        int MaxLineSpread,
        IReadOnlyList<PartitionStructure> Partitions);

    public sealed record LineConflict(
        string File,
        string Method,
        int Ordinal,
        int Path,
        string Partition,
        IReadOnlyList<int> Lines);

    /// <summary>
    /// Per (File, Method) (ordinal, path) structure per partition. Divergent=true
    /// means at least one observed partition has a different pair set, including
    /// path-only changes even when its ordinals and pair count are unchanged.
    /// MaxLineSpread is the largest per-identity line spread of the method across
    /// partitions; a small spread is consistent with PDB line-attribution drift,
    /// while a large spread flags a potential mid-sequence decision shift.
    /// </summary>
    public IReadOnlyList<MethodStructure> MethodStructures()
    {
        var result = new List<MethodStructure>();
        foreach (var kv in _structures)
        {
            var partitions = kv.Value
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => new PartitionStructure(p.Key, p.Value.ToArray()))
                .ToArray();

            var baseline = kv.Value.Values.First();
            var divergent = kv.Value.Values.Any(pairs => !baseline.SetEquals(pairs));

            var maxSpread = 0;
            foreach (var id in _lines)
            {
                if (id.Key.File != kv.Key.File || id.Key.Method != kv.Key.Method) { continue; }

                var min = int.MaxValue;
                var max = int.MinValue;
                foreach (var lines in id.Value.Values)
                {
                    foreach (var line in lines)
                    {
                        if (line < min) { min = line; }

                        if (line > max) { max = line; }
                    }
                }

                if (max - min > maxSpread) { maxSpread = max - min; }
            }

            result.Add(new MethodStructure(kv.Key.File, kv.Key.Method, divergent, maxSpread, partitions));
        }

        return result
            .OrderBy(m => m.File, StringComparer.Ordinal)
            .ThenBy(m => m.Method, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Identities observed with more than one distinct source line inside a single
    /// partition (two modules compiled the same source differently within one
    /// variant). Cross-partition line differences are expected (attribution drift);
    /// within-partition conflicts are invalid source mappings and fail validation.
    /// </summary>
    public IReadOnlyList<LineConflict> WithinPartitionLineConflicts()
    {
        var result = new List<LineConflict>();
        foreach (var kv in _lines)
        {
            foreach (var partition in kv.Value.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (partition.Value.Count > 1)
                {
                    result.Add(new LineConflict(
                        kv.Key.File,
                        kv.Key.Method,
                        kv.Key.Ordinal,
                        kv.Key.Path,
                        partition.Key,
                        partition.Value.ToArray()));
                }
            }
        }

        return result
            .OrderBy(c => c.File, StringComparer.Ordinal)
            .ThenBy(c => c.Method, StringComparer.Ordinal)
            .ThenBy(c => c.Ordinal)
            .ThenBy(c => c.Path)
            .ThenBy(c => c.Partition, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>All partitions observed, sorted.</summary>
    public IReadOnlyList<string> Partitions => _partitions.ToArray();

    /// <summary>Rejects ambiguous mappings inside a caller-defined build partition.</summary>
    public void Validate()
    {
        var conflicts = WithinPartitionLineConflicts();
        if (conflicts.Count > 0)
        {
            var first = conflicts[0];
            throw new InvalidOperationException($"{conflicts.Count} within-partition line conflict(s): {first.File} :: {first.Method} ordinal={first.Ordinal} path={first.Path} partition={first.Partition} lines={string.Join(',', first.Lines)}.");
        }
    }
}
