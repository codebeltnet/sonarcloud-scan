using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CoverageNormalizer;

/// <summary>OpenCover evidence to one deterministic logical source coverage report.</summary>
public static class Program
{
    /// <summary>Runs the CLI, returning nonzero on incomplete or ambiguous evidence.</summary>
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "--verify-artifacts")
            {
                if (args.Length is not (3 or 4)) { throw new ArgumentException("--verify-artifacts <report.xml> <summary.json> [sonar-measures.json]"); }
                AcceptanceVerifier.Verify(args[1], args[2], args.Length == 4 ? args[3] : null);
                Console.WriteLine("PASS: persisted coverage models reconcile exactly.");
                return 0;
            }
            return Run(args);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidOperationException or System.Xml.XmlException or FormatException or OverflowException)
        {
            Console.Error.WriteLine("FAIL: " + exception.Message);
            return 3;
        }
    }

    /// <summary>Executes normalization; repository acceptance bounds are explicit caller inputs, never algorithm defaults.</summary>
    public static int Run(string[] args)
    {
        string? inputDir = null, fileList = null, outputPath = null, summaryPath = null, diagPath = null, divergencePath = null, partitionPattern = null;
        string? productPrefix = null;
        var sourceRoot = "src";
        var requirePartition = false;
        int? maximumSpread = null;
        var expectations = new SortedDictionary<string, long>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            string Next()
            {
                if (i + 1 >= args.Length) { throw new ArgumentException($"Missing value for '{args[i]}'."); }
                return args[++i];
            }

            switch (args[i])
            {
                case "--in": inputDir = Next(); break;
                case "--file-list": fileList = Next(); break;
                case "--out": outputPath = Next(); break;
                case "--summary-json": summaryPath = Next(); break;
                case "--diag-csv": diagPath = Next(); break;
                case "--divergence-csv": divergencePath = Next(); break;
                case "--partition-regex": partitionPattern = Next(); break;
                case "--require-partition-match": requirePartition = true; break;
                case "--product-prefix": productPrefix = Next(); break;
                case "--source-root": sourceRoot = Next(); break;
                case "--max-identity-spread":
                    maximumSpread = int.Parse(Next(), CultureInfo.InvariantCulture);
                    if (maximumSpread < 0) { throw new ArgumentException("--max-identity-spread must be nonnegative."); }
                    break;
                case "--expect-product-sequence-lines-covered":
                case "--expect-product-sequence-lines-total":
                case "--expect-product-sonar-lines-covered":
                case "--expect-product-sonar-lines-total":
                case "--expect-product-branches-covered":
                case "--expect-product-branches-total":
                    var key = args[i];
                    var value = long.Parse(Next(), CultureInfo.InvariantCulture);
                    if (value < 0) { throw new ArgumentException($"{key} must be nonnegative."); }
                    expectations.Add(key, value);
                    break;
                default: throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }

        sourceRoot = PathNormalizer.ValidateSourceRoot(sourceRoot);
        productPrefix ??= sourceRoot + "/";
        if (outputPath is null) { throw new ArgumentException("--out <report.xml> is required."); }
        if ((inputDir is null) == (fileList is null)) { throw new ArgumentException("Exactly one of --in <dir> or --file-list <path> is required."); }
        if (requirePartition && partitionPattern is null) { throw new ArgumentException("--require-partition-match requires --partition-regex."); }
        var partitionRegex = partitionPattern is null ? null : new Regex(partitionPattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));
        var root = inputDir is not null ? Path.GetFullPath(inputDir) : Path.GetDirectoryName(Path.GetFullPath(fileList!))!;
        var reports = inputDir is not null
            ? Directory.EnumerateFiles(root, "*opencover*.xml", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal).ToArray()
            : File.ReadLines(fileList!).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'))
                .Select(l => Path.GetFullPath(Path.IsPathRooted(l) ? l : Path.Combine(root, l))).ToArray();
        if (reports.Length == 0) { throw new InvalidOperationException("No OpenCover reports found; refusing an empty report."); }

        var model = new CoverageModel();
        var diagnostics = new Diagnostics();
        var stats = new List<ReaderStats>();
        var failures = new List<string>();
        long bytes = 0;
        foreach (var report in reports)
        {
            bytes += new FileInfo(report).Length;
            var relative = Path.GetRelativePath(root, report).Replace('\\', '/');
            var partition = "all";
            if (partitionRegex is not null)
            {
                var match = partitionRegex.Match(relative);
                if (!match.Success)
                {
                    partition = "unmatched";
                    if (requirePartition) { failures.Add($"Report path '{relative}' did not match --partition-regex."); }
                }
                else
                {
                    var groups = match.Groups.Cast<Group>().Skip(1).Select(g => g.Value).ToArray();
                    partition = groups.Length == 0 ? match.Value : string.Join('+', groups);
                }
            }

            try
            {
                stats.Add(OpenCoverReader.Read(report, partition, model, diagnostics, sourceRoot));
            }
            catch (UnresolvedSourceException exception)
            {
                stats.Add(exception.Stats);
                failures.Add(exception.Message);
                break; // retain diagnostics, but never continue consuming invalid evidence
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.Xml.XmlException)
            {
                failures.Add($"Report '{relative}': {exception.Message}");
                break;
            }
        }

        var summary = CoverageSummary.Create(model, diagnostics, stats, reports.Length, bytes, productPrefix);
        var conflicts = diagnostics.WithinPartitionLineConflicts();
        var structures = diagnostics.MethodStructures();
        if (model.LineObservations == 0 && model.BranchObservations == 0) { failures.Add("Parsed reports contained no source observations."); }
        try { diagnostics.Validate(); }
        catch (InvalidOperationException exception) { failures.Add(exception.Message); }
        if (summary.UnmatchedFileIds > 0) { failures.Add($"unmatchedFileIds={summary.UnmatchedFileIds}; incomplete source mapping."); }
        if (maximumSpread.HasValue && summary.MaxIdentitySpread > maximumSpread.Value)
        {
            failures.Add($"Caller acceptance bound exceeded: maxIdentitySpread={summary.MaxIdentitySpread}, limit={maximumSpread.Value}.");
        }

        foreach (var expectation in expectations)
        {
            var actual = expectation.Key switch
            {
                "--expect-product-sequence-lines-covered" => summary.Product.SequenceLinesCovered,
                "--expect-product-sequence-lines-total" => summary.Product.SequenceLinesTotal,
                "--expect-product-sonar-lines-covered" => summary.Product.SonarLinesCovered,
                "--expect-product-sonar-lines-total" => summary.Product.SonarLinesTotal,
                "--expect-product-branches-covered" => summary.Product.LogicalBranchesCovered,
                "--expect-product-branches-total" => summary.Product.LogicalBranchesTotal,
                _ => throw new InvalidOperationException("Unsupported expectation."),
            };
            if (expectation.Value != actual) { failures.Add($"{expectation.Key}: expected {expectation.Value}, got {actual}."); }
        }

        // Diagnostics survive failure. No coverage report is written from rejected evidence.
        if (summaryPath is not null) { EnsureDirectory(summaryPath); summary.Write(summaryPath); }
        if (diagPath is not null)
        {
            var csv = new StringBuilder("File,Method,Ordinal,Path,MinLine,MaxLine,Observations,Covered\n");
            foreach (var detail in model.BranchDetails())
            {
                csv.Append(Csv(detail.File)).Append(',').Append(Csv(detail.Method)).Append(',')
                    .Append(FormattableString.Invariant($"{detail.Ordinal},{detail.Path},{detail.MinLine},{detail.MaxLine},{detail.Observations},{(detail.Covered ? "true" : "false")}\n"));
            }
            WriteCsv(diagPath, csv);
        }
        if (divergencePath is not null)
        {
            var csv = new StringBuilder("File,Method,Divergent,MaxLineSpread,Partition,PairCount,OrdinalPathPairs\n");
            foreach (var method in structures)
            {
                foreach (var partition in method.Partitions)
                {
                    var signature = string.Join(';', partition.Pairs.Select(p => FormattableString.Invariant($"{p.Ordinal}:{p.Path}")));
                    csv.Append(Csv(method.File)).Append(',').Append(Csv(method.Method)).Append(',')
                        .Append(method.Divergent ? "true" : "false").Append(',')
                        .Append(method.MaxLineSpread.ToString(CultureInfo.InvariantCulture)).Append(',').Append(Csv(partition.Partition)).Append(',')
                        .Append(partition.Pairs.Count.ToString(CultureInfo.InvariantCulture)).Append(',').Append(Csv(signature)).Append('\n');
                }
            }
            WriteCsv(divergencePath, csv);
        }

        Console.WriteLine(FormattableString.Invariant($"CoverageNormalizer: reports={reports.Length}, bytes={bytes}, partitions={summary.Partitions}"));
        Console.WriteLine(FormattableString.Invariant($"  observations: lines={model.LineObservations}, branches={model.BranchObservations}"));
        PrintMetrics("ALL", summary.All);
        PrintMetrics("PRODUCT", summary.Product);
        Console.WriteLine(FormattableString.Invariant($"  diagnostics: unmatchedFileIds={summary.UnmatchedFileIds}, withinPartitionLineConflicts={conflicts.Count}, divergentMethods={summary.DivergentMethods} ((ordinal,path) sets), maxIdentitySpread={summary.MaxIdentitySpread}"));
        foreach (var method in structures.Where(m => m.Divergent))
        {
            Console.WriteLine(FormattableString.Invariant($"    divergent: {method.File} :: {method.Method}, spread={method.MaxLineSpread}"));
        }

        if (failures.Count > 0)
        {
            foreach (var failure in failures) { Console.Error.WriteLine("FAIL: " + failure); }
            return 3;
        }
        EnsureDirectory(outputPath);
        SonarQubeReportWriter.Write(model, outputPath);
        Console.WriteLine("PASS: logical coverage normalized deterministically.");
        return 0;
    }

    private static void PrintMetrics(string label, CoverageMetrics metrics)
    {
        Console.WriteLine(FormattableString.Invariant($"  {label} sequence lines: {metrics.SequenceLinesCovered}/{metrics.SequenceLinesTotal}"));
        Console.WriteLine(FormattableString.Invariant($"  {label} emitted Sonar lines: {metrics.SonarLinesCovered}/{metrics.SonarLinesTotal} ({metrics.SonarLineCoverage:F4}%), branch-only lines={metrics.BranchOnlyLines}"));
        Console.WriteLine(FormattableString.Invariant($"  {label} logical branches: {metrics.LogicalBranchesCovered}/{metrics.LogicalBranchesTotal} ({metrics.SonarBranchCoverage:F4}%), emitted headline={metrics.SonarHeadlineCoverage:F4}%"));
    }

    private static void EnsureDirectory(string path) => Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    private static void WriteCsv(string path, StringBuilder content)
    {
        EnsureDirectory(path);
        File.WriteAllText(path, content.ToString(), new UTF8Encoding(false));
    }
}
