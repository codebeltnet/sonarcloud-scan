# Analyze with SonarCloud

Uses the [SonarScanner for .NET tool](https://www.nuget.org/packages/dotnet-sonarscanner) to hook into the build pipeline, downloads SonarCloud quality profiles and settings, and prepares your project for analysis.

> This action is part of the Codebelt umbrella and ensures a consistent way of: 
> 
> - Defining your CI/CD pipeline 
> - Structuring your repository
> - Keeping your codebase small and feasible
> - Writing clean and maintainable code
> - Deploying your code to different environments
> - Automating as much as possible
>
> A paved path to excel as a DevSecOps Engineer.

## Usage

To use this action in your GitHub repository, you can follow these steps:

```yaml
uses: codebeltnet/sonarcloud-scan@v2
```

### Inputs

```yaml
with:
  # The SonarCloud generated token.
  token:
  # The key of your project in SonarCloud.
  projectKey:
  # The name of your organization in SonarCloud.
  organization:
  # The version of your project, e.g. 1.0.0.
  version:
  # The host URL of your SonarCloud instance.
  host: 'https://sonarcloud.io'
  # Additional properties to be passed to the scanner.
  parameters: >-
    -d:sonar.exclusions='**/obj/**,**/bin/**'
  # Coverage representation: opencover (default) or normalized (opt in).
  coverage-mode: opencover
  # Repository-relative source directory, used only in normalized mode.
  coverage-source-root: src
  # Artifact-relative path capture groups identify build variant and target framework.
  coverage-partition-regex: '^([^/]+?)(?:-[0-9a-f]{16,})?/([^/]+)/'
  # Optional caller acceptance guard; empty means no maximum source-line spread.
  coverage-max-identity-spread: ''
```

### Outputs

This action has no outputs.

## Examples

### Prepare SonarCloud

```yaml
steps:
  - name: Run SonarCloud Analysis
    uses: codebeltnet/sonarcloud-scan@v2
    with:
      token: ${{ secrets.SONAR_TOKEN }}
      organization: geekle
      projectKey: savvyio
      version: ${{ needs.build.outputs.version }}
```

### Normalize overlapping coverage evidence

Install Bash, the SonarScanner for .NET tool and the SDKs required by your build before using this action. The default mode retains its existing Bash prerequisite. Normalized mode additionally requires PowerShell 7 and a stable .NET 10 SDK; prerequisites remain caller-managed. GitHub-hosted runners provide PowerShell 7. `jobs-sonarcloud@v3` installs the supported SDKs and scanner for its callers.

```yaml
- uses: codebeltnet/sonarcloud-scan@v2
  with:
    token: ${{ secrets.SONAR_TOKEN }}
    organization: your-organization
    projectKey: your-project
    version: ${{ needs.build.outputs.version }}
    coverage-mode: normalized
    coverage-source-root: src
```

Both modes download `TestResults*` once, retaining each artifact directory. The default `opencover` mode retains `sonar.cs.opencover.reportsPaths=<workspace>/artifacts/TestResults*/**/*opencover*.xml` and the existing VSTest glob. Existing callers need no new inputs.

Normalized mode streams `**/*opencover*.xml` from those artifact directories into exactly one `artifacts/CoverageNormalized/SonarQube.report.xml`, reconciles its persisted counters with `summary.json`, then begins analysis with only `sonar.coverageReportPaths`. VSTest reports remain configured in both modes. Raw reports remain intact. Caller build and scanner finalization follow this action as before.

Source paths use `/` separators and the first complete `coverage-source-root` directory marker, preserving case. For example, Windows `D:\agent\repo\src\Product\A.cs`, Linux `/home/runner/repo/src/Product/A.cs` and macOS `/Users/runner/repo/src/Product/A.cs` become `src/Product/A.cs`. Nested roots such as `packages/source` work too. Paths outside the source root retain the proven fallback of removing leading separators. No source lines absent from reports are fabricated; Sonar calculates production coverage over its normal source universe.

The partition regex operates on paths relative to `artifacts`. Its capture groups are joined with `+` to identify one compilation variant. The default groups `TestResults-Debug-Linux-X64/net10.0/...` by artifact variant and target framework, stripping optional hexadecimal artifact suffixes. Every report must match. If your artifact layout differs, supply a pattern that separates configuration, OS, architecture and target framework as applicable. Partition identity affects conflict validation, not execution union.

`coverage-max-identity-spread` is an optional nonnegative integer. An empty value imposes no caller-specific bound. Parser errors, unresolved source IDs and conflicting lines within a partition always fail; cross-partition line drift remains diagnostic unless the caller's spread bound is exceeded. Missing artifacts, missing reports, missing/empty output, failed reconciliation and unsupported modes fail with actionable annotations.

`parameters` accepts whitespace-separated scanner arguments with single or double quotes. Arguments are passed literally; shell expansion and commands are never evaluated. Both `sonar.cs.opencover.reportsPaths` and `sonar.coverageReportPaths` are action-owned and forbidden in `parameters`, including same-mode overrides. This ensures exactly one coverage model.

See [normalizer semantics](tooling/CoverageNormalizer/README.md) and [validation provenance and compiler-divergence limitation](docs/coverage-normalization.md). Codecov continues consuming raw coverage independently; this action does not normalize its input.

## Caller workflows to showcase the Codebelt experience

### Basic CI/CD Pipeline

- Bootstrapper API - https://github.com/codebeltnet/bootstrapper/blob/main/.github/workflows/pipelines.yml
- Extensions for Asp.Versioning API - https://github.com/codebeltnet/asp-versioning/blob/main/.github/workflows/pipelines.yml
- Extensions for AWS Signature Version 4 API - https://github.com/codebeltnet/aws-signature-v4/blob/main/.github/workflows/pipelines.yml
- Extensions for Globalization API - https://github.com/codebeltnet/globalization/blob/main/.github/workflows/pipelines.yml
- Extensions for Newtonsoft.Json API - https://github.com/codebeltnet/newtonsoft-json/blob/main/.github/workflows/pipelines.yml
- Extensions for Swashbuckle.AspNetCore API - https://github.com/codebeltnet/swashbuckle-aspnetcore/blob/main/.github/workflows/pipelines.yml
- Extensions for xUnit API - https://github.com/codebeltnet/xunit/blob/main/.github/workflows/pipelines.yml
- Extensions for YamlDotNet API - https://github.com/codebeltnet/yamldotnet/blob/main/.github/workflows/pipelines.yml
- Shared Kernel API - https://github.com/codebeltnet/shared-kernel/blob/main/.github/workflows/pipelines.yml
- Unitify API - https://github.com/codebeltnet/unitify/blob/main/.github/workflows/pipelines.yml

### Intermediate CI/CD Pipeline

- Savvy I/O - https://github.com/codebeltnet/savvyio/blob/main/.github/workflows/pipelines.yml

### Advanced CI/CD Pipeline

- Cuemon for .NET - https://github.com/gimlichael/Cuemon/blob/main/.github/workflows/pipelines.yml

## Contributing to Analyze with SonarCloud from Codebelt

Contributions are welcome! 
Feel free to submit issues, feature requests, or pull requests to help improve this action.

### License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

> [!TIP]
> To learn more about the Codebelt experience and offerings, visit our [organization page](https://github.com/codebeltnet) on GitHub.
