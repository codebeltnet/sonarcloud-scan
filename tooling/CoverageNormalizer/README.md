# CoverageNormalizer

Converts raw OpenCover evidence to one deterministic Sonar generic coverage v1 report. Requires a stable .NET 10 SDK. The implementation and original tests were ported from the verified reference recorded in [validation provenance](../../docs/coverage-normalization.md).

Collect comprehensively. Union execution evidence. Deduplicate logical source structure. Report coverage once.

## Model

The streaming reader consumes every OpenCover element exactly once. File IDs are module-scoped, methods must be named, source point attributes must be valid and source IDs must resolve. Hidden/non-source points are counted and skipped. Invalid or incomplete evidence fails; it is never accepted as uncovered data.

Sequence execution is unioned by `(File, Line)`. Logical branch identity is `(File, Method, Ordinal, Path)`, with execution unioned across every observation. Each identity is attributed deterministically to its minimum observed source line. Structural diagnostics compare the exact `(ordinal,path)` sets per method and compilation partition, including path-only changes with equal pair counts. Conflicting source lines for an identity inside one partition fail. Cross-partition line drift is recorded; a caller may bound it with `--max-identity-spread`. There is no universal spread threshold.

Generic coverage represents branches on `lineToCover` entries. Branch-only lines are emitted when necessary. Sequence coverage wins on lines with sequence evidence; otherwise any covered branch outcome covers a branch-only line. `branchOnlyLines` counts distinct extra lines. Line, branch and headline counters are derived from the same emitted model.

`--source-root` defaults to `src` and accepts a nonempty repository-relative directory, including nested roots. Workspace paths use `/` separators and the first complete source-root marker, preserving case. Outside-root paths retain the proven fallback of removing leading separators. The summary's default product scope is `<source-root>/`; the CLI's optional `--product-prefix` can narrow diagnostic verification without excluding files from XML.

Output uses UTF-8 without BOM and LF. XML, JSON and CSV ordering is deterministic and independent of input order or culture. Summary schema v2 retains separate `all` and `product` counters: sequence lines, emitted Sonar lines, logical branches, branch-only lines and percentages. Root diagnostics include parser counts, unmatched IDs, partitions, conflicts, divergent methods and the identity-spread distribution. No timestamps or workspace paths appear in the summary.

## Usage

Run from this `tooling` directory so its .NET 10 SDK contract is selected:

```pwsh
dotnet build CoverageNormalizer/CoverageNormalizer.csproj -c Release
dotnet test --project CoverageNormalizer.Tests/CoverageNormalizer.Tests.csproj -c Release -- --minimum-expected-tests 55
dotnet run --project CoverageNormalizer/CoverageNormalizer.csproj -c Release --no-build -- --in /path/to/artifacts --out /path/to/artifacts/CoverageNormalized/SonarQube.report.xml --summary-json /path/to/artifacts/CoverageNormalized/summary.json --source-root src --partition-regex '^([^/]+?)(?:-[0-9a-f]{16,})?/([^/]+)/' --require-partition-match
dotnet run --project CoverageNormalizer/CoverageNormalizer.csproj -c Release --no-build -- --verify-artifacts /path/to/artifacts/CoverageNormalized/SonarQube.report.xml /path/to/artifacts/CoverageNormalized/summary.json
```

Exactly one of `--in <directory>` and `--file-list <path>` is required. A file-list's directory defines the partition root; entries may be relative to it or absolute. `--partition-regex` capture groups are joined with `+`; `--require-partition-match` rejects unmatched reports. Without a regex, reports form one partition. Define partitions by compilation dimensions to distinguish valid cross-variant drift from within-variant conflicts.

Optional `--diag-csv` and `--divergence-csv` persist branch identities and full partition structure signatures. Optional expected counters are CLI acceptance aids, never shared action inputs or algorithm defaults. Diagnostics survive rejected evidence; a report is written only after validation succeeds. Exit code 3 indicates rejected evidence or configuration.

`--verify-artifacts` independently parses persisted XML and summary and reconciles exact counters and percentages. An optional third file containing Sonar API measures supports exact comparison only when the analyzed source universe matches the report scope. Production Sonar may add report-absent coverable lines as uncovered; the shared action does not assert service percentage parity.

## Validation and limitations

The original 44 xUnit v3 cases remain, covering parser fidelity, unresolved IDs, execution union, structural comparison, conflicts, drift, generic XML counters, encoding, reconciliation and all six input permutations across cultures. Additional cases cover configurable source roots and invalid roots. `CoverageNormalizer.Tests/Verify-OpenCoverEvidence.ps1` supplies an independent DOM oracle for a single report. Action contract fixtures execute the real normalizer and reconciliation while replacing only the external scanner.

Compiler-divergent methods can make ordinal/path correlation ambiguous. Structural diagnostics expose divergence but cannot reconstruct compiler-independent decision ownership. A spread bound detects worsening attribution drift; it does not prove semantic identity or bound every possible mismerge. See [known limitation and provenance](../../docs/coverage-normalization.md).
