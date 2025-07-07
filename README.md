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
uses: codebeltnet/sonarcloud-scan@v1
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
```

### Outputs

This action has no outputs.

## Examples

### Prepare SonarCloud

```yaml
steps:
  - name: Run SonarCloud Analysis
    uses: codebeltnet/sonarcloud-scan@v1
    with:
      token: ${{ secrets.SONAR_TOKEN }}
      organization: geekle
      projectKey: savvyio
      version: ${{ needs.build.outputs.version }}
```

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
