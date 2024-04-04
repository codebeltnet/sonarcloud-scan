# Analyze with SonarCloud

Uses the [SonarScanner for .NET tool](https://www.nuget.org/packages/dotnet-sonarscanner) to hook into the build pipeline, downloads SonarCloud quality profiles and settings, and prepares your project for analysis.

> This action is part of the Codebelt ecosystem and ensures a consistent way of: 
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
uses: codebeltnet/sonarcloud-scan@main
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
    -d:sonar.sources='src/'
    -d:sonar.tests='test/'
```

### Outputs

This action has no outputs.

## Examples

### Prepare SonarCloud

```yaml
steps:
  - name: Run SonarCloud Analysis
    uses: codebeltnet/sonarcloud-scan@main
    with:
      token: ${{ secrets.SONAR_TOKEN }}
      organization: geekle
      projectKey: savvyio
      version: ${{ needs.build.outputs.version }}
```

## Contributing to Analyze with SonarCloud

Contributions are welcome! 
Feel free to submit issues, feature requests, or pull requests to help improve this action.

### License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
