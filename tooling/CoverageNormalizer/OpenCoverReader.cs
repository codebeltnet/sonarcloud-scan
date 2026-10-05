using System.Globalization;
using System.Text;
using System.Xml;

namespace CoverageNormalizer;

/// <summary>Per-report parse counters retained in diagnostic summaries.</summary>
public sealed record ReaderStats(int Modules, int Files, int Methods, long SequencePoints, long BranchPoints, long SkippedPoints, int UnmatchedFileIds);

/// <summary>Unresolved source evidence, with parse counters retained for diagnostics.</summary>
public sealed class UnresolvedSourceException : InvalidOperationException
{
    /// <summary>Initializes a failure retaining report statistics.</summary>
    public UnresolvedSourceException(string reportPath, ReaderStats stats)
        : base($"{stats.UnmatchedFileIds} unmatched fileid reference(s) in '{reportPath}'; refusing incomplete source evidence.")
    {
        Stats = stats;
    }

    /// <summary>Gets the counters of the rejected report.</summary>
    public ReaderStats Stats { get; }
}

/// <summary>
/// Streaming reader for Coverlet/OpenCover coverage XML. File IDs are scoped to
/// modules; method names are read only within methods. A pure forward scan consumes
/// every element exactly once, without subtree read-ahead. Source points require
/// valid attributes and resolvable file IDs. Non-source sl&lt;=0 and the PDB hidden
/// sentinel 0xFEEFEE are counted and skipped intentionally.
/// </summary>
public static class OpenCoverReader
{
    /// <summary>
    /// Reads one OpenCover report, adding every observation to <paramref name="model"/>
    /// and structure evidence to <paramref name="diagnostics"/> under the given
    /// partition label. Failures throw; partial reports must never be silently accepted.
    /// </summary>
    public static ReaderStats Read(string reportPath, string partition, CoverageModel model, Diagnostics diagnostics, string sourceRoot = "src")
    {
        sourceRoot = PathNormalizer.ValidateSourceRoot(sourceRoot);
        var fileMap = new Dictionary<string, string>(StringComparer.Ordinal); // uid -> normalized path, per module
        var modules = 0;
        var files = 0;
        var methods = 0;
        long sequencePoints = 0;
        long branchPoints = 0;
        long skippedPoints = 0;
        var unmatchedFileIds = 0;

        diagnostics.RecordPartition(partition);
        string? currentMethod = null;
        var insideMethod = false;
        var sawSession = false;
        var readingName = false;
        var nameBuilder = new StringBuilder();

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreWhitespace = true,
            XmlResolver = null,
        };

        using var reader = XmlReader.Create(reportPath, settings);
        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    if (reader.Depth == 0)
                    {
                        if (reader.Name != "CoverageSession")
                        {
                            throw new InvalidOperationException($"Expected CoverageSession root in '{reportPath}'.");
                        }

                        sawSession = true;
                    }

                    switch (reader.Name)
                    {
                        case "Module":
                            modules++;
                            fileMap.Clear(); // file uids are scoped to their module
                            break;

                        case "File":
                            var uid = RequireText(reader, "uid");
                            var fullPath = RequireText(reader, "fullPath");
                            if (!fileMap.TryAdd(uid, PathNormalizer.Normalize(fullPath, sourceRoot)))
                            {
                                throw new InvalidOperationException($"Duplicate File uid '{uid}' inside one module in '{reportPath}'.");
                            }

                            files++;

                            break;

                        case "Method":
                            currentMethod = null; // filled by its <Name> child
                            insideMethod = !reader.IsEmptyElement;
                            readingName = false;
                            methods++;
                            break;

                        case "Name" when insideMethod && currentMethod is null && !reader.IsEmptyElement:
                            readingName = true;
                            nameBuilder.Clear();
                            break;

                        case "SequencePoint":
                            sequencePoints++;
                            var sequenceLine = RequireInt(reader, "sl");
                            var sequenceVisits = RequireLong(reader, "vc");
                            if (sequenceLine <= 0 || sequenceLine == 0xFEEFEE)
                            {
                                skippedPoints++; // hidden sequence point: no source line
                                break;
                            }

                            if (TryResolveFile(reader, fileMap, out var sequenceFile, ref unmatchedFileIds))
                            {
                                MethodName(); // source evidence must belong to a named method
                                model.Add(new LineObservation(sequenceFile, sequenceLine, sequenceVisits > 0));
                            }

                            break;

                        case "BranchPoint":
                            branchPoints++;
                            var branchLine = RequireInt(reader, "sl");
                            var branchVisits = RequireLong(reader, "vc");
                            var ordinal = RequireNonNegativeInt(reader, "ordinal");
                            var path = RequireNonNegativeInt(reader, "path");
                            if (branchLine <= 0 || branchLine == 0xFEEFEE)
                            {
                                skippedPoints++; // hidden branch point: no source line
                                break;
                            }

                            if (TryResolveFile(reader, fileMap, out var branchFile, ref unmatchedFileIds))
                            {
                                var observation = new BranchObservation(
                                    branchFile,
                                    MethodName(),
                                    ordinal,
                                    path,
                                    branchLine,
                                    branchVisits > 0);
                                model.Add(observation);
                                diagnostics.Record(partition, observation);
                            }

                            break;
                    }

                    break;

                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.SignificantWhitespace:
                    if (readingName)
                    {
                        nameBuilder.Append(reader.Value);
                    }

                    break;

                case XmlNodeType.EndElement:
                    switch (reader.Name)
                    {
                        case "Name" when readingName:
                            currentMethod = nameBuilder.ToString();
                            if (string.IsNullOrWhiteSpace(currentMethod))
                            {
                                throw new InvalidOperationException($"Empty method Name in '{reportPath}'.");
                            }
                            readingName = false;
                            break;

                        case "Method":
                            currentMethod = null;
                            insideMethod = false;
                            readingName = false;
                            break;

                        case "Module":
                            fileMap.Clear();
                            break;
                    }

                    break;
            }
        }

        if (!sawSession)
        {
            throw new InvalidOperationException($"Missing CoverageSession in '{reportPath}'.");
        }

        var stats = new ReaderStats(modules, files, methods, sequencePoints, branchPoints, skippedPoints, unmatchedFileIds);
        if (unmatchedFileIds > 0)
        {
            throw new UnresolvedSourceException(reportPath, stats);
        }

        return stats;

        string MethodName()
        {
            return currentMethod ?? throw new InvalidOperationException(
                $"Branch/sequence point encountered outside a named method in '{reportPath}'; report structure is not supported.");
        }
    }

    private static bool TryResolveFile(XmlReader reader, Dictionary<string, string> fileMap, out string file, ref int unmatchedFileIds)
    {
        var fileid = reader.GetAttribute("fileid");
        if (fileid is null || !fileMap.TryGetValue(fileid, out file!))
        {
            file = string.Empty;
            unmatchedFileIds++;
            return false;
        }

        return true;
    }

    private static string RequireText(XmlReader reader, string name)
    {
        var raw = reader.GetAttribute(name);
        return !string.IsNullOrWhiteSpace(raw) ? raw
            : throw new InvalidOperationException($"Missing nonempty attribute '{name}' on '{reader.Name}' in '{reader.BaseURI}'.");
    }

    private static int RequireNonNegativeInt(XmlReader reader, string name)
    {
        var value = RequireInt(reader, name);
        return value >= 0 ? value
            : throw new InvalidOperationException($"Negative '{name}' on '{reader.Name}' in '{reader.BaseURI}'.");
    }

    private static int RequireInt(XmlReader reader, string name)
    {
        var raw = reader.GetAttribute(name);
        return raw is not null && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidOperationException($"Point is missing integer attribute '{name}' (element '{reader.Name}' in '{((XmlReader)reader).BaseURI}').");
    }

    private static long RequireLong(XmlReader reader, string name)
    {
        var raw = reader.GetAttribute(name);
        return raw is not null && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidOperationException($"Point is missing integer attribute '{name}' (element '{reader.Name}').");
    }
}
