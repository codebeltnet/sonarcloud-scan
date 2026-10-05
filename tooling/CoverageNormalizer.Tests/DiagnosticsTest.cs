using Codebelt.Extensions.Xunit;
using Xunit;

namespace CoverageNormalizer;

public class DiagnosticsTest : Test
{
    public DiagnosticsTest(ITestOutputHelper output) : base(output) { }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(0, 1, true)]
    [InlineData(1, 0, true)]
    public void MethodStructures_ShouldCompareExactOrdinalPathPairs(int ordinal, int path, bool divergent)
    {
        var diagnostics = new Diagnostics();
        diagnostics.Record("A", new BranchObservation("src/A.cs", "M", 0, 0, 10, false));
        diagnostics.Record("B", new BranchObservation("src/A.cs", "M", ordinal, path, 10, false));
        var structure = Assert.Single(diagnostics.MethodStructures());
        Assert.Equal(divergent, structure.Divergent);
    }

    [Fact]
    public void MethodStructures_ShouldDetectAnAdditionalPairEvenWhenOrdinalsMatch()
    {
        var diagnostics = new Diagnostics();
        var observation = new BranchObservation("src/A.cs", "M", 0, 0, 10, false);
        diagnostics.Record("A", observation);
        diagnostics.Record("B", observation);
        diagnostics.Record("B", observation with { Path = 1 });
        Assert.True(Assert.Single(diagnostics.MethodStructures()).Divergent);
    }

    [Fact]
    public void Validate_ShouldFailOnWithinPartitionLineConflict()
    {
        var diagnostics = new Diagnostics();
        var observation = new BranchObservation("src/A.cs", "M", 0, 0, 10, false);
        diagnostics.Record("A", observation);
        diagnostics.Record("A", observation with { Line = 12 });
        Assert.Equal(new[] { 10, 12 }, Assert.Single(diagnostics.WithinPartitionLineConflicts()).Lines);
        Assert.Throws<InvalidOperationException>(diagnostics.Validate);
    }

    [Fact]
    public void Validate_ShouldKeepCrossPartitionDriftDiagnosticNotStructural()
    {
        var diagnostics = new Diagnostics();
        var observation = new BranchObservation("src/A.cs", "M", 0, 0, 10, false);
        diagnostics.Record("A", observation);
        diagnostics.Record("B", observation with { Line = 40 });
        diagnostics.Validate();
        Assert.Empty(diagnostics.WithinPartitionLineConflicts());
        var structure = Assert.Single(diagnostics.MethodStructures());
        Assert.False(structure.Divergent);
        Assert.Equal(30, structure.MaxLineSpread);
    }
}
