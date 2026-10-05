using Codebelt.Extensions.Xunit;
using Xunit;

namespace CoverageNormalizer;

public class PathNormalizerTest : Test
{
    public PathNormalizerTest(ITestOutputHelper output) : base(output) { }

    [Theory]
    [InlineData("/home/runner/work/repo/src/Product/A.cs", "src/Product/A.cs")]
    [InlineData("D:\\agent\\repo\\src\\Product\\A.cs", "src/Product/A.cs")]
    [InlineData("src/Product/A.cs", "src/Product/A.cs")]
    [InlineData("/different/root/src/Product/A.cs", "src/Product/A.cs")]
    [InlineData("/_/shared/NullableAttributes.cs", "_/shared/NullableAttributes.cs")]
    [InlineData("test/Product/A.cs", "test/Product/A.cs")]
    public void Normalize_ShouldCanonicalizeSourcePaths(string input, string expected)
    {
        Assert.Equal(expected, PathNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("/Users/runner/work/repo/lib/Product/A.cs", "lib", "lib/Product/A.cs")]
    [InlineData("D:\\agent\\repo\\packages\\source\\A.cs", "packages/source", "packages/source/A.cs")]
    [InlineData("/workspace/lib.special/A.cs", "lib.special/", "lib.special/A.cs")]
    [InlineData("/workspace/notlib/A.cs", "lib", "workspace/notlib/A.cs")]
    [InlineData("lib/A.cs", "lib", "lib/A.cs")]
    public void Normalize_ShouldUseConfiguredSourceRoot(string input, string sourceRoot, string expected)
    {
        Assert.Equal(expected, PathNormalizer.Normalize(input, sourceRoot));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/src")]
    [InlineData("C:\\src")]
    [InlineData("../src")]
    [InlineData("src/../lib")]
    [InlineData("src//lib")]
    public void Normalize_ShouldRejectInvalidSourceRoot(string sourceRoot)
    {
        Assert.Throws<ArgumentException>(() => PathNormalizer.Normalize("src/A.cs", sourceRoot));
    }
}
