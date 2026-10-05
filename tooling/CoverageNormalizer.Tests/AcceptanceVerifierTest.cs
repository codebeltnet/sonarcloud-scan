using System.Text.Json;
using Codebelt.Extensions.Xunit;
using Xunit;

namespace CoverageNormalizer;

public class AcceptanceVerifierTest : Test
{
    public AcceptanceVerifierTest(ITestOutputHelper output) : base(output) { }

    [Theory]
    [InlineData(1, true)]
    [InlineData(140, false)]
    public void Verify_ShouldRequireExactSonarDenominators(int sonarLines, bool matches)
    {
        using var workspace = new TestWorkspace();
        var model = new CoverageModel();
        model.Add(new LineObservation("src/A.cs", 10, true));
        model.Add(new BranchObservation("src/A.cs", "M", 0, 0, 10, true));
        var diagnostics = new Diagnostics();
        diagnostics.RecordPartition("p");
        var summary = CoverageSummary.Create(model, diagnostics, new[] { new ReaderStats(1, 1, 1, 1, 1, 0, 0) }, 1, 100, "src/");
        var xml = workspace.PathOf("SonarQube.report.xml");
        var json = workspace.PathOf("summary.json");
        summary.Write(json);
        SonarQubeReportWriter.Write(model, xml);
        var measures = workspace.Write("measures.json", JsonSerializer.Serialize(new
        {
            component = new { measures = new[]
            {
                new { metric = "lines_to_cover", value = sonarLines.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                new { metric = "uncovered_lines", value = "0" },
                new { metric = "conditions_to_cover", value = "1" },
                new { metric = "uncovered_conditions", value = "0" },
                new { metric = "line_coverage", value = "100.0" },
                new { metric = "branch_coverage", value = "100.0" },
                new { metric = "coverage", value = "100.0" },
            } }
        }));
        AcceptanceVerifier.Verify(xml, json);
        if (matches) { AcceptanceVerifier.Verify(xml, json, measures); }
        else { Assert.Throws<InvalidOperationException>(() => AcceptanceVerifier.Verify(xml, json, measures)); }
        File.WriteAllText(xml, File.ReadAllText(xml).Replace("covered=\"true\"", "covered=\"false\""));
        Assert.Throws<InvalidOperationException>(() => AcceptanceVerifier.Verify(xml, json));
    }
}
