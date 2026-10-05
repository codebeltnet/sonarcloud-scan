using Codebelt.Extensions.Xunit;
using Xunit;

namespace CoverageNormalizer;

public class OpenCoverReaderTest : Test
{
    public OpenCoverReaderTest(ITestOutputHelper output) : base(output) { }

    [Theory]
    [InlineData("<SequencePoint sl=\"10\" fileid=\"1\"/>")]
    [InlineData("<SequencePoint sl=\"x\" vc=\"1\" fileid=\"1\"/>")]
    [InlineData("<SequencePoint vc=\"1\" fileid=\"1\"/>")]
    [InlineData("<SequencePoint sl=\"10\" vc=\"bad\" fileid=\"1\"/>")]
    [InlineData("<BranchPoint sl=\"10\" vc=\"1\" fileid=\"1\" path=\"0\"/>")]
    [InlineData("<BranchPoint sl=\"10\" vc=\"1\" fileid=\"1\" ordinal=\"0\"/>")]
    [InlineData("<BranchPoint sl=\"10\" vc=\"1\" fileid=\"1\" ordinal=\"-1\" path=\"0\"/>")]
    [InlineData("<BranchPoint sl=\"10\" vc=\"1\" fileid=\"1\" ordinal=\"0\" path=\"x\"/>")]
    public void Read_ShouldFailOnMissingOrInvalidPointAttributes(string point)
    {
        using var workspace = new TestWorkspace();
        var path = workspace.Write("bad.opencover.xml", TestWorkspace.Report(TestWorkspace.Module(points: point)));
        Assert.Throws<InvalidOperationException>(() => OpenCoverReader.Read(path, "p", new CoverageModel(), new Diagnostics()));
    }

    [Theory]
    [InlineData("<File uid=\"1\"/>")]
    [InlineData("<File fullPath=\"src/A.cs\"/>")]
    [InlineData("<File uid=\"\" fullPath=\"src/A.cs\"/>")]
    [InlineData("<File uid=\"1\" fullPath=\"src/A.cs\"/><File uid=\"1\" fullPath=\"src/B.cs\"/>")]
    public void Read_ShouldFailOnInvalidFileMapping(string files)
    {
        using var workspace = new TestWorkspace();
        var path = workspace.Write("bad.opencover.xml", TestWorkspace.Report("<Module><Files>" + files + "</Files></Module>"));
        Assert.Throws<InvalidOperationException>(() => OpenCoverReader.Read(path, "p", new CoverageModel(), new Diagnostics()));
    }

    [Theory]
    [InlineData("fileid=\"99\"")]
    [InlineData("")]
    public void Read_ShouldFailOnUnresolvedFileIdsAndRetainStats(string fileid)
    {
        using var workspace = new TestWorkspace();
        var point = $"<SequencePoint sl=\"10\" vc=\"1\" {fileid}/>";
        var path = workspace.Write("bad.opencover.xml", TestWorkspace.Report(TestWorkspace.Module(points: point)));
        var exception = Assert.Throws<UnresolvedSourceException>(() => OpenCoverReader.Read(path, "p", new CoverageModel(), new Diagnostics()));
        Assert.Equal(1, exception.Stats.UnmatchedFileIds);
        Assert.Equal(1, exception.Stats.SequencePoints);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<Name/>")]
    [InlineData("<Name> </Name>")]
    public void Read_ShouldRejectUnnamedSourceMethods(string name)
    {
        using var workspace = new TestWorkspace();
        var module = TestWorkspace.Module(points: "<SequencePoint sl=\"10\" vc=\"1\" fileid=\"1\"/>")
            .Replace("<Name>System.Void Product.A::Run()</Name>", name);
        var path = workspace.Write("bad.opencover.xml", TestWorkspace.Report(module));
        Assert.Throws<InvalidOperationException>(() => OpenCoverReader.Read(path, "p", new CoverageModel(), new Diagnostics()));
    }

    [Fact]
    public void Read_ShouldIntentionallySkipHiddenAndNonSourcePoints()
    {
        using var workspace = new TestWorkspace();
        var points = "<SequencePoint sl=\"0\" vc=\"1\"/><SequencePoint sl=\"-1\" vc=\"0\"/><SequencePoint sl=\"16707566\" vc=\"1\"/>";
        var branches = "<BranchPoint sl=\"16707566\" vc=\"0\" ordinal=\"0\" path=\"0\"/>";
        var path = workspace.Write("hidden.opencover.xml", TestWorkspace.Report(TestWorkspace.Module(points: points, branches: branches)));
        var model = new CoverageModel();
        var stats = OpenCoverReader.Read(path, "p", model, new Diagnostics());
        Assert.Equal(4, stats.SkippedPoints);
        Assert.Equal(0, stats.UnmatchedFileIds);
        Assert.Empty(model.Files);
    }

    [Theory]
    [InlineData("<NotOpenCover/>")]
    [InlineData("<CoverageSession>")]
    [InlineData("<!DOCTYPE CoverageSession [<!ENTITY x 'x'>]><CoverageSession/>")]
    public void Read_ShouldRejectWrongRootMalformedXmlAndDtd(string xml)
    {
        using var workspace = new TestWorkspace();
        var path = workspace.Write("bad.xml", xml);
        Assert.ThrowsAny<Exception>(() => OpenCoverReader.Read(path, "p", new CoverageModel(), new Diagnostics()));
    }

    [Fact]
    public void Read_ShouldParseEveryPointAndScopeFileIdsToModules()
    {
        using var workspace = new TestWorkspace();
        var points = "<SequencePoint sl=\"10\" vc=\"0\" fileid=\"1\"/><SequencePoint sl=\"11\" vc=\"2\" fileid=\"1\"/>";
        var branches = "<BranchPoint sl=\"10\" vc=\"0\" fileid=\"1\" ordinal=\"0\" path=\"0\"/><BranchPoint sl=\"10\" vc=\"3\" fileid=\"1\" ordinal=\"1\" path=\"1\"/>";
        var path = workspace.Write("coverage.opencover.xml", TestWorkspace.Report(TestWorkspace.Module(points: points, branches: branches), TestWorkspace.Module("C:/other/src/Product/B.cs", points, branches)));
        var model = new CoverageModel();
        var diagnostics = new Diagnostics();
        var stats = OpenCoverReader.Read(path, "partition", model, diagnostics);
        Assert.Equal(new ReaderStats(2, 2, 2, 4, 4, 0, 0), stats);
        Assert.Equal(new[] { "src/Product/A.cs", "src/Product/B.cs" }, model.Files);
        Assert.Equal((2L, 4L), model.SequenceLineTotals());
        Assert.Equal((2L, 4L), model.BranchTotals());
    }
}
