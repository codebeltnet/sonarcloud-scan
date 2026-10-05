using System.Text;
using System.Text.Json;

namespace CoverageNormalizer;

/// <summary>Coverage counters for an explicit source-file scope; percentages are 0..100 or null for empty denominators.</summary>
public sealed record CoverageMetrics(long SequenceLinesCovered, long SequenceLinesTotal,
    long SonarLinesCovered, long SonarLinesTotal, long LogicalBranchesCovered, long LogicalBranchesTotal,
    long BranchOnlyLines, double? SonarLineCoverage, double? SonarBranchCoverage, double? SonarHeadlineCoverage)
{
    /// <summary>Calculates counters from the same line model consumed by the XML writer.</summary>
    public static CoverageMetrics Calculate(CoverageModel model, IEnumerable<string> files)
    {
        long sequenceCovered = 0, sequenceTotal = 0, sonarCovered = 0, sonarTotal = 0;
        long branchCovered = 0, branchTotal = 0, branchOnly = 0;
        foreach (var file in files.OrderBy(f => f, StringComparer.Ordinal))
        {
            var sequence = model.SequenceLineTotals(file);
            sequenceCovered += sequence.Covered;
            sequenceTotal += sequence.Total;
            foreach (var line in model.SonarLinesOf(file))
            {
                sonarTotal++;
                if (line.Covered) { sonarCovered++; }
                if (!line.HasSequencePoint) { branchOnly++; }
                branchTotal += line.BranchesToCover;
                branchCovered += line.CoveredBranches;
            }
        }

        return new CoverageMetrics(sequenceCovered, sequenceTotal, sonarCovered, sonarTotal, branchCovered, branchTotal,
            branchOnly, Percentage(sonarCovered, sonarTotal), Percentage(branchCovered, branchTotal),
            Percentage(sonarCovered + branchCovered, sonarTotal + branchTotal));
    }

    private static double? Percentage(long covered, long total) => total == 0 ? null : 100.0 * covered / total;
}

/// <summary>Machine-consumable schema v2. Root coverage is all files; Product is the caller-selected Sonar acceptance scope.</summary>
public sealed record CoverageSummary(int SchemaVersion, int Reports, int ReportsParsed, long ReportBytes, int Partitions,
    long Modules, long Files, long Methods, long RawSequencePoints, long RawBranchPoints, long SkippedPoints,
    long LineObservations, long BranchObservations, CoverageMetrics All, string ProductPrefix,
    int ProductFiles, CoverageMetrics Product, long UnmatchedFileIds, int WithinPartitionLineConflicts,
    int DivergentMethods, int MaxIdentitySpread, IReadOnlyDictionary<int, long> IdentityLineSpread)
{
    /// <summary>Creates a summary without input-order-dependent persisted state.</summary>
    public static CoverageSummary Create(CoverageModel model, Diagnostics diagnostics, IReadOnlyList<ReaderStats> stats,
        int reports, long reportBytes, string productPrefix)
    {
        var spread = new SortedDictionary<int, long>();
        foreach (var detail in model.BranchDetails())
        {
            var width = detail.MaxLine - detail.MinLine;
            spread[width] = spread.TryGetValue(width, out var count) ? count + 1 : 1;
        }

        var productFiles = model.Files.Where(f => f.StartsWith(productPrefix, StringComparison.Ordinal)).ToArray();
        return new CoverageSummary(2, reports, stats.Count, reportBytes, diagnostics.Partitions.Count,
            stats.Sum(s => (long)s.Modules), stats.Sum(s => (long)s.Files), stats.Sum(s => (long)s.Methods),
            stats.Sum(s => s.SequencePoints), stats.Sum(s => s.BranchPoints), stats.Sum(s => s.SkippedPoints),
            model.LineObservations, model.BranchObservations, CoverageMetrics.Calculate(model, model.Files),
            productPrefix, productFiles.Length, CoverageMetrics.Calculate(model, productFiles),
            stats.Sum(s => (long)s.UnmatchedFileIds), diagnostics.WithinPartitionLineConflicts().Count,
            diagnostics.MethodStructures().Count(m => m.Divergent), spread.Keys.DefaultIfEmpty(0).Max(), spread);
    }

    /// <summary>Writes stable camel-case JSON, LF, UTF-8 without BOM; no timestamps or workspace paths.</summary>
    public void Write(string path)
    {
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        File.WriteAllText(path, JsonSerializer.Serialize(this, options).Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
    }
}
