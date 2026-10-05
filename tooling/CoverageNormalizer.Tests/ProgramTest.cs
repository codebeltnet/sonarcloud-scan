using System.Globalization;
using System.Text.Json;
using Codebelt.Extensions.Xunit;
using Xunit;

namespace CoverageNormalizer;

public class ProgramTest : Test
{
    public ProgramTest(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void Run_ShouldEmitByteIdenticalXmlJsonAndCsvForInputPermutations()
    {
        using var workspace = new TestWorkspace();
        var reports = new[]
        {
            workspace.Write("a.opencover.xml", TestWorkspace.Report(TestWorkspace.Module(points: "<SequencePoint sl=\"10\" vc=\"0\" fileid=\"1\"/>", branches: "<BranchPoint sl=\"10\" vc=\"0\" fileid=\"1\" ordinal=\"0\" path=\"0\"/>"))),
            workspace.Write("b.opencover.xml", TestWorkspace.Report(TestWorkspace.Module(points: "<SequencePoint sl=\"10\" vc=\"1\" fileid=\"1\"/>", branches: "<BranchPoint sl=\"40\" vc=\"1\" fileid=\"1\" ordinal=\"0\" path=\"0\"/>"))),
            workspace.Write("c.opencover.xml", TestWorkspace.Report(TestWorkspace.Module(branches: "<BranchPoint sl=\"11\" vc=\"0\" fileid=\"1\" ordinal=\"0\" path=\"1\"/>"))),
        };
        var orders = new[] { new[] { 0, 1, 2 }, new[] { 2, 1, 0 }, new[] { 1, 0, 2 }, new[] { 2, 0, 1 }, new[] { 0, 2, 1 }, new[] { 1, 2, 0 } };
        byte[][]? baseline = null;
        foreach (var order in orders)
        {
            var list = workspace.Write("order.txt", string.Join('\n', order.Select(i => reports[i])));
            var paths = new[] { workspace.PathOf("SonarQube.report.xml"), workspace.PathOf("summary.json"), workspace.PathOf("identities.csv"), workspace.PathOf("structures.csv") };
            var previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(order[0] == 0 ? "en-US" : "da-DK");
                Assert.Equal(0, Program.Run(new[] { "--file-list", list, "--out", paths[0], "--summary-json", paths[1], "--diag-csv", paths[2], "--divergence-csv", paths[3], "--partition-regex", "^([abc])\\.", "--require-partition-match", "--expect-product-sonar-lines-total", "2" }));
            }
            finally { CultureInfo.CurrentCulture = previousCulture; }
            var current = paths.Select(File.ReadAllBytes).ToArray();
            if (baseline is null) { baseline = current; }
            else { for (var i = 0; i < paths.Length; i++) { Assert.Equal(baseline[i], current[i]); } }
        }
        using var summary = JsonDocument.Parse(File.ReadAllBytes(workspace.PathOf("summary.json")));
        Assert.Equal(2, summary.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(30, summary.RootElement.GetProperty("maxIdentitySpread").GetInt32()); // no universal spread limit
        Assert.Equal(3, summary.RootElement.GetProperty("partitions").GetInt32());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Run_ShouldWriteDiagnosticsThenFailWithoutCoverageOnInvalidMapping(bool unresolved)
    {
        using var workspace = new TestWorkspace();
        var point = unresolved ? "<SequencePoint sl=\"10\" vc=\"1\" fileid=\"99\"/>" : "<SequencePoint sl=\"10\" vc=\"1\" fileid=\"1\"/>";
        var branches = unresolved ? "" : "<BranchPoint sl=\"10\" vc=\"1\" fileid=\"1\" ordinal=\"0\" path=\"0\"/><BranchPoint sl=\"12\" vc=\"0\" fileid=\"1\" ordinal=\"0\" path=\"0\"/>";
        workspace.Write("bad.opencover.xml", TestWorkspace.Report(TestWorkspace.Module(points: point, branches: branches)));
        var report = workspace.PathOf("SonarQube.report.xml");
        var summaryPath = workspace.PathOf("diagnostics/summary.json");
        Assert.Equal(3, Program.Run(new[] { "--in", workspace.Root, "--out", report, "--summary-json", summaryPath, "--diag-csv", workspace.PathOf("diagnostics/identities.csv"), "--divergence-csv", workspace.PathOf("diagnostics/structures.csv") }));
        Assert.False(File.Exists(report));
        using var summary = JsonDocument.Parse(File.ReadAllBytes(summaryPath));
        Assert.Equal(unresolved ? 1 : 0, summary.RootElement.GetProperty("unmatchedFileIds").GetInt32());
        Assert.Equal(unresolved ? 0 : 1, summary.RootElement.GetProperty("withinPartitionLineConflicts").GetInt32());
        Assert.True(File.Exists(workspace.PathOf("diagnostics/identities.csv")));
        Assert.True(File.Exists(workspace.PathOf("diagnostics/structures.csv")));
    }

    [Theory]
    [InlineData("--max-identity-spread", "1")]
    [InlineData("--expect-product-sonar-lines-total", "999")]
    public void Run_ShouldEnforceCallerAcceptanceRules(string option, string value)
    {
        using var workspace = new TestWorkspace();
        foreach (var item in new[] { (Name: "a", Line: 10), (Name: "b", Line: 12) })
        {
            workspace.Write(item.Name + ".opencover.xml", TestWorkspace.Report(TestWorkspace.Module(branches: $"<BranchPoint sl=\"{item.Line}\" vc=\"1\" fileid=\"1\" ordinal=\"0\" path=\"0\"/>")));
        }
        Assert.Equal(3, Program.Run(new[] { "--in", workspace.Root, "--out", workspace.PathOf("out.xml"), "--partition-regex", "^([ab])", option, value }));
    }
}
