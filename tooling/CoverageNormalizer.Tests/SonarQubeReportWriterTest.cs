using System.Text;
using System.Xml.Linq;
using Codebelt.Extensions.Xunit;
using Xunit;

namespace CoverageNormalizer;

public class SonarQubeReportWriterTest : Test
{
    public SonarQubeReportWriterTest(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void Write_ShouldPersistTheExactSonarModelInDeterministicOrder()
    {
        using var workspace = new TestWorkspace();
        var model = new CoverageModel();
        model.Add(new LineObservation("src/Z.cs", 9, true));
        model.Add(new LineObservation("src/A.cs", 2, false));
        model.Add(new BranchObservation("src/A.cs", "M", 0, 0, 1, true));
        model.Add(new BranchObservation("src/A.cs", "M", 1, 1, 1, false));
        model.Add(new BranchObservation("src/A.cs", "N", 0, 0, 2, true));
        model.Add(new BranchObservation("src/A.cs", "N", 1, 1, 3, false));
        var path = workspace.PathOf("SonarQube.report.xml");
        SonarQubeReportWriter.Write(model, path);
        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes.Take(3).SequenceEqual(new byte[] { 239, 187, 191 }));
        Assert.DoesNotContain("\r", Encoding.UTF8.GetString(bytes));
        var files = XDocument.Load(path).Root!.Elements("file").ToArray();
        Assert.Equal(new[] { "src/A.cs", "src/Z.cs" }, files.Select(f => (string)f.Attribute("path")!));
        var lines = files.SelectMany(f => f.Elements("lineToCover")).ToArray();
        Assert.Equal(new[] { 1, 2, 3 }, files[0].Elements().Select(l => (int)l.Attribute("lineNumber")!));
        Assert.True((bool)lines[0].Attribute("covered")!); // visited branch-only line
        Assert.False((bool)lines[1].Attribute("covered")!); // sequence evidence wins
        Assert.False((bool)lines[2].Attribute("covered")!); // unvisited branch-only line
        Assert.Equal(2, (int)lines[0].Attribute("branchesToCover")!);
        Assert.Equal(1, (int)lines[0].Attribute("coveredBranches")!);
        Assert.Null(lines[3].Attribute("branchesToCover"));
        var metrics = CoverageMetrics.Calculate(model, model.Files);
        Assert.Equal(1, metrics.SequenceLinesCovered);
        Assert.Equal(2, metrics.SequenceLinesTotal);
        Assert.Equal(lines.LongLength, metrics.SonarLinesTotal);
        Assert.Equal(lines.LongCount(l => (bool)l.Attribute("covered")!), metrics.SonarLinesCovered);
        Assert.Equal(lines.Sum(l => (long?)l.Attribute("branchesToCover") ?? 0), metrics.LogicalBranchesTotal);
        Assert.Equal(lines.Sum(l => (long?)l.Attribute("coveredBranches") ?? 0), metrics.LogicalBranchesCovered);
        Assert.Equal(2, metrics.BranchOnlyLines);
        Assert.Equal(50, metrics.SonarLineCoverage);
        Assert.Equal(50, metrics.SonarBranchCoverage);
        Assert.Equal(50, metrics.SonarHeadlineCoverage);
        Assert.Equal((2L, 4L), model.SonarLineTotals());
    }
}
