using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace CoverageNormalizer;

/// <summary>Independent reconciliation of persisted generic coverage and summary counters.</summary>
public static class AcceptanceVerifier
{
    /// <summary>
    /// Verifies XML counters against both summary scopes, then optional Sonar product
    /// measures. Integer counters are exact; Sonar percentages are compared at its
    /// published one-decimal precision. Missing metrics are failures, not zeroes.
    /// </summary>
    public static void Verify(string reportPath, string summaryPath, string? sonarMeasuresPath = null)
    {
        var summary = JsonSerializer.Deserialize<CoverageSummary>(File.ReadAllText(summaryPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Missing summary.");
        if (summary.SchemaVersion != 2) { throw new InvalidOperationException("Expected summary schemaVersion=2."); }
        if (summary.ReportsParsed != summary.Reports || summary.UnmatchedFileIds != 0 || summary.WithinPartitionLineConflicts != 0)
        {
            throw new InvalidOperationException("Summary contains rejected or incomplete source evidence.");
        }

        var root = XDocument.Load(reportPath).Root ?? throw new InvalidOperationException("Missing XML root.");
        if (root.Name != "coverage" || (string?)root.Attribute("version") != "1") { throw new InvalidOperationException("Expected generic coverage version 1."); }
        var files = root.Elements("file").ToArray();
        if (files.Select(f => (string?)f.Attribute("path")).Distinct(StringComparer.Ordinal).Count() != files.Length)
        {
            throw new InvalidOperationException("Duplicate file entries.");
        }
        foreach (var file in files)
        {
            var lines = file.Elements("lineToCover").ToArray();
            if (lines.Select(l => (int)l.Attribute("lineNumber")!).Distinct().Count() != lines.Length)
            {
                throw new InvalidOperationException($"Duplicate line entries in {(string?)file.Attribute("path")}.");
            }
        }
        VerifyScope("all", files, summary.All);
        var productFiles = files.Where(f => ((string?)f.Attribute("path"))?.StartsWith(summary.ProductPrefix, StringComparison.Ordinal) == true).ToArray();
        Equal("productFiles", summary.ProductFiles, productFiles.Length);
        VerifyScope("product", productFiles, summary.Product);
        if (sonarMeasuresPath is null) { return; }

        using var json = JsonDocument.Parse(File.ReadAllBytes(sonarMeasuresPath));
        var measures = json.RootElement.GetProperty("component").GetProperty("measures").EnumerateArray()
            .ToDictionary(m => m.GetProperty("metric").GetString()!, m => m.GetProperty("value").GetString()!, StringComparer.Ordinal);
        long Count(string key) => long.Parse(measures[key], CultureInfo.InvariantCulture);
        var product = summary.Product;
        Equal("Sonar lines_to_cover", product.SonarLinesTotal, Count("lines_to_cover"));
        Equal("Sonar uncovered_lines", product.SonarLinesTotal - product.SonarLinesCovered, Count("uncovered_lines"));
        Equal("Sonar conditions_to_cover", product.LogicalBranchesTotal, Count("conditions_to_cover"));
        Equal("Sonar uncovered_conditions", product.LogicalBranchesTotal - product.LogicalBranchesCovered, Count("uncovered_conditions"));
        foreach (var metric in new[] { (Name: "line_coverage", Value: product.SonarLineCoverage), (Name: "branch_coverage", Value: product.SonarBranchCoverage), (Name: "coverage", Value: product.SonarHeadlineCoverage) })
        {
            var expected = metric.Value ?? throw new InvalidOperationException($"No denominator for {metric.Name}.");
            var actual = double.Parse(measures[metric.Name], CultureInfo.InvariantCulture);
            if (Math.Round(expected, 1, MidpointRounding.AwayFromZero) != actual)
            {
                throw new InvalidOperationException($"Sonar {metric.Name}: expected {expected:F6} at one decimal, got {actual}.");
            }
        }
    }

    private static void VerifyScope(string scope, IEnumerable<XElement> files, CoverageMetrics expected)
    {
        var lines = files.SelectMany(f => f.Elements("lineToCover")).ToArray();
        Equal(scope + " sonarLinesTotal", expected.SonarLinesTotal, lines.LongLength);
        Equal(scope + " sonarLinesCovered", expected.SonarLinesCovered, lines.LongCount(l => (bool)l.Attribute("covered")!));
        Equal(scope + " logicalBranchesTotal", expected.LogicalBranchesTotal, lines.Sum(l => (long?)l.Attribute("branchesToCover") ?? 0));
        Equal(scope + " logicalBranchesCovered", expected.LogicalBranchesCovered, lines.Sum(l => (long?)l.Attribute("coveredBranches") ?? 0));
        Equal(scope + " sequence + branch-only lines", expected.SonarLinesTotal, expected.SequenceLinesTotal + expected.BranchOnlyLines);
        Percentage(scope + " sonarLineCoverage", expected.SonarLineCoverage, expected.SonarLinesCovered, expected.SonarLinesTotal);
        Percentage(scope + " sonarBranchCoverage", expected.SonarBranchCoverage, expected.LogicalBranchesCovered, expected.LogicalBranchesTotal);
        Percentage(scope + " sonarHeadlineCoverage", expected.SonarHeadlineCoverage,
            expected.SonarLinesCovered + expected.LogicalBranchesCovered, expected.SonarLinesTotal + expected.LogicalBranchesTotal);
    }

    private static void Percentage(string metric, double? actual, long covered, long total)
    {
        double? expected = total == 0 ? null : 100.0 * covered / total;
        if (actual != expected) { throw new InvalidOperationException($"{metric}: expected {expected}, got {actual}."); }
    }

    private static void Equal(string metric, long expected, long actual)
    {
        if (expected != actual) { throw new InvalidOperationException($"{metric}: expected {expected}, got {actual}."); }
    }
}
