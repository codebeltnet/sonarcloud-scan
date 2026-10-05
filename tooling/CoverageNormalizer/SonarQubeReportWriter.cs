using System.Globalization;
using System.Text;
using System.Xml;

namespace CoverageNormalizer;

/// <summary>
/// Deterministic writer for the SonarQube generic coverage format (version 1).
/// Output is byte-identical for any input ordering: files are sorted ordinally,
/// lines ascending, and every aggregate (covered = OR, counts = sum) is
/// order-independent. Line endings are LF and the encoding is UTF-8 without BOM
/// regardless of host OS.
/// </summary>
public static class SonarQubeReportWriter
{
    /// <summary>
    /// Writes one &lt;lineToCover&gt; per coverable line. Each logical branch
    /// (one merged identity) contributes exactly once to branchesToCover on its
    /// attributed line and once to coveredBranches when any execution covered it.
    /// </summary>
    public static void Write(CoverageModel model, string outputPath)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        using var writer = XmlWriter.Create(outputPath, settings);
        writer.WriteStartDocument();
        writer.WriteStartElement("coverage");
        writer.WriteAttributeString("version", "1");

        foreach (var file in model.Files)
        {
            writer.WriteStartElement("file");
            writer.WriteAttributeString("path", file);

            foreach (var line in model.SonarLinesOf(file))
            {
                writer.WriteStartElement("lineToCover");
                writer.WriteAttributeString("lineNumber", line.Line.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("covered", line.Covered ? "true" : "false");
                if (line.BranchesToCover > 0)
                {
                    writer.WriteAttributeString("branchesToCover", line.BranchesToCover.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("coveredBranches", line.CoveredBranches.ToString(CultureInfo.InvariantCulture));
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndDocument();
    }
}
