using System.Text;

namespace CoverageNormalizer;

internal sealed class TestWorkspace : IDisposable
{
    public string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CoverageNormalizer-" + Guid.NewGuid().ToString("N"));

    public TestWorkspace()
    {
        Directory.CreateDirectory(Root);
    }

    public string PathOf(string name) => System.IO.Path.Combine(Root, name);

    public string Write(string name, string content)
    {
        var path = PathOf(name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    public static string Module(string file = "/work/src/Product/A.cs", string points = "", string branches = "", string method = "System.Void Product.A::Run()") =>
        $"<Module><Files><File uid=\"1\" fullPath=\"{file}\" /></Files><Classes><Class><Methods><Method><Name>{method}</Name><SequencePoints>{points}</SequencePoints><BranchPoints>{branches}</BranchPoints></Method></Methods></Class></Classes></Module>";

    public static string Report(params string[] modules) => "<CoverageSession><Modules>" + string.Concat(modules) + "</Modules></CoverageSession>";

    public void Dispose()
    {
        Directory.Delete(Root, true);
    }
}
