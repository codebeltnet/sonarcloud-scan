using Codebelt.Extensions.Xunit;
using Xunit;

namespace CoverageNormalizer;

public class CoverageModelTest : Test
{
    public CoverageModelTest(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void Add_ShouldUnionLinesWithoutMultiplyingDenominator()
    {
        var model = new CoverageModel();
        model.Add(new LineObservation("src/A.cs", 10, false));
        model.Add(new LineObservation("src/A.cs", 10, true));
        model.Add(new LineObservation("src/A.cs", 10, false));
        Assert.Equal((1L, 1L), model.SequenceLineTotals());
        Assert.Equal(3, model.LineObservations);
    }

    [Fact]
    public void Add_ShouldUnionBranchesAndKeepEveryIdentityDimensionDistinct()
    {
        var model = new CoverageModel();
        var original = new BranchObservation("src/A.cs", "M", 0, 0, 10, false);
        model.Add(original);
        model.Add(original with { Covered = true, Line = 12 });
        model.Add(original);
        model.Add(original with { Path = 1 });
        model.Add(original with { Ordinal = 1 });
        model.Add(original with { Method = "N" });
        model.Add(original with { File = "src/B.cs" });
        Assert.Equal((1L, 5L), model.BranchTotals());
        Assert.Equal(7, model.BranchObservations);
        var detail = Assert.Single(model.BranchDetails().Where(d => d.File == original.File && d.Method == "M" && d.Ordinal == 0 && d.Path == 0));
        Assert.Equal(10, detail.MinLine);
        Assert.Equal(12, detail.MaxLine);
        Assert.Equal(3, detail.Observations);
    }
}
