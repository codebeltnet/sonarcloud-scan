using System.Text.RegularExpressions;

namespace CoverageNormalizer;

/// <summary>
/// Canonical repository-relative path normalization for coverage source paths.
/// Separators become '/'; the first complete source-root marker anchors the
/// repository-relative form. Paths outside that root only lose leading separators.
/// Source spelling and case are preserved on Windows, Linux and macOS.
/// </summary>
public static class PathNormalizer
{
    /// <summary>Validates a nonempty repository-relative source directory.</summary>
    public static string ValidateSourceRoot(string sourceRoot)
    {
        var root = sourceRoot.Replace('\\', '/').TrimEnd('/');
        if (string.IsNullOrWhiteSpace(root) || root.StartsWith('/') || root.Contains(':') ||
            root.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new ArgumentException("--source-root must be a nonempty repository-relative directory without '.' or '..' segments.");
        }
        return root;
    }

    /// <summary>Normalizes a raw OpenCover fullPath to canonical repository-relative form.</summary>
    public static string Normalize(string rawFullPath, string sourceRoot = "src")
    {
        var root = ValidateSourceRoot(sourceRoot);
        var p = rawFullPath.Replace('\\', '/');
        var m = Regex.Match(p, @"(^|/)" + Regex.Escape(root) + "/", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));
        return m.Success ? root + "/" + p[(m.Index + m.Length)..] : p.TrimStart('/');
    }
}
