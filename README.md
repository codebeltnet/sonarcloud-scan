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
    uses: codebeltnet/sonarcloud-scan@v1
    with:
      token: ${{ secrets.SONAR_TOKEN }}
      organization: geekle
      projectKey: savvyio
      version: ${{ needs.build.outputs.version }}
```

### Sample workflow for .NET Class Library

```yaml
name: Generic CI/CD Pipeline (.NET Library)
on:
  push:
    branches: [main]
    paths-ignore:
      - .codecov
      - .docfx
      - .github
      - .nuget
  pull_request:
    branches: [main]
  workflow_dispatch:
    inputs:
      configuration:
        type: choice
        description: The build configuration to use in the deploy stage.
        required: true
        default: Release
        options:
          - Debug
          - Release

jobs:
  build:
    name: 🛠️ Build
    runs-on: ubuntu-22.04
    outputs:
      version: ${{ steps.minver-calculate.outputs.version }}
    steps:
      - name: Checkout
        uses: codebeltnet/git-checkout@v1

      - name: Install .NET
        uses: codebeltnet/install-dotnet@v1

      - name: Install MinVer
        uses: codebeltnet/dotnet-tool-install-minver@v1

      - id: minver-calculate
        name: Calculate Version
        uses: codebeltnet/minver-calculate@v1

      - name: Download strongname.snk file
        uses: codebeltnet/gcp-download-file@v1
        with: 
          serviceAccountKey: ${{ secrets.GCP_TOKEN }}
          bucketName: ${{ secrets.GCP_BUCKETNAME }}
          objectName: strongname.snk

      - name: Restore Dependencies
        uses: codebeltnet/dotnet-restore@v1

      - name: Build for Preview
        uses: codebeltnet/dotnet-build@v1
        with:
          configuration: Debug

      - name: Build for Production
        uses: codebeltnet/dotnet-build@v1
        with:
          configuration: Release

  pack:
    name: 📦 Pack
    runs-on: ubuntu-22.04
    strategy:
      matrix:
        configuration: [Debug, Release]
    needs: [build]
    steps:     
      - name: Pack for ${{ matrix.configuration }}
        uses: codebeltnet/dotnet-pack@v1
        with:
          configuration: ${{ matrix.configuration }}
          uploadPackedArtifact: true
          version: ${{ needs.build.outputs.version }}

  test:
    name: 🧪 Test
    needs: [build]
    strategy:
      matrix:
        os: [ubuntu-22.04, windows-2022]
    runs-on: ${{ matrix.os }}
    steps:
      - name: Checkout
        uses: codebeltnet/git-checkout@v1

      - name: Install .NET
        uses: codebeltnet/install-dotnet@v1

      - name: Install .NET Tool - Report Generator
        uses: codebeltnet/dotnet-tool-install-reportgenerator@v1

      - name: Test with Debug build
        uses: codebeltnet/dotnet-test@v1
        with:
          configuration: Debug
          buildSwitches: -p:SkipSignAssembly=true

      - name: Test with Release build
        uses: codebeltnet/dotnet-test@v1
        with:
          configuration: Release
          buildSwitches: -p:SkipSignAssembly=true

  sonarcloud:
    name: 🔬 Code Quality Analysis
    needs: [build,test]
    runs-on: ubuntu-22.04
    steps:
      - name: Checkout
        uses: codebeltnet/git-checkout@v1

      - name: Install .NET
        uses: codebeltnet/install-dotnet@v1

      - name: Install .NET Tool - Sonar Scanner
        uses: codebeltnet/dotnet-tool-install-sonarscanner@v1

      - name: Restore Dependencies
        uses: codebeltnet/dotnet-restore@v1

      - name: Run SonarCloud Analysis
        uses: codebeltnet/sonarcloud-scan@v1
        with:
          token: ${{ secrets.SONAR_TOKEN }}
          organization: your-sonarcloud-organization
          projectKey: your-sonarcloud-project-key
          version: ${{ needs.build.outputs.version }}

      - name: Build
        uses: codebeltnet/dotnet-build@v1
        with:
          buildSwitches: -p:SkipSignAssembly=true
          uploadBuildArtifact: false

      - name: Finalize SonarCloud Analysis
        uses: codebeltnet/sonarcloud-scan-finalize@v1
        with:
          token: ${{ secrets.SONAR_TOKEN }}

  codecov:
    name: 📊 Code Coverage Analysis
    needs: [build,test]
    runs-on: ubuntu-22.04
    steps:
      - name: Checkout
        uses: codebeltnet/git-checkout@v1

      - name: Run CodeCov Analysis
        uses: codebeltnet/codecov-scan@v1
        with:
          token: ${{ secrets.CODECOV_TOKEN }}
          repository: your-github-repository
          
  codeql:
    name: 🛡️ Security Analysis
    needs: [build,test]
    runs-on: ubuntu-22.04
    steps:
      - name: Checkout
        uses: codebeltnet/git-checkout@v1

      - name: Install .NET
        uses: codebeltnet/install-dotnet@v1

      - name: Restore Dependencies
        uses: codebeltnet/dotnet-restore@v1

      - name: Prepare CodeQL SAST Analysis
        uses: codebeltnet/codeql-scan@v1

      - name: Build
        uses: codebeltnet/dotnet-build@v1
        with:
          buildSwitches: -p:SkipSignAssembly=true
          uploadBuildArtifact: false

      - name: Finalize CodeQL SAST Analysis
        uses: codebeltnet/codeql-scan-finalize@v1

  deploy:
    name: 🚀 Deploy v${{ needs.build.outputs.version }}
    runs-on: ubuntu-22.04
    needs: [build,pack,test,sonarcloud,codecov,codeql]
    environment: Production
    steps:
      - uses: codebeltnet/nuget-push@v1
        with:
          token: ${{ secrets.NUGET_TOKEN }}
          configuration: ${{ inputs.configuration == '' && 'Release' || inputs.configuration }}

```

## Contributing to Analyze with SonarCloud

Contributions are welcome! 
Feel free to submit issues, feature requests, or pull requests to help improve this action.

### License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

### Other Actions

:bookmark: [Analyze with Codecov](https://github.com/codebeltnet/codecov-scan)\
:bookmark: [Analyze with CodeQL](https://github.com/codebeltnet/codeql-scan)\
:bookmark: [Finalyze with CodeQL](https://github.com/codebeltnet/codeql-scan-finalize)\
:bookmark: [Docker Compose](https://github.com/codebeltnet/docker-compose)\
:bookmark: [.NET Build](https://github.com/codebeltnet/dotnet-build)\
:bookmark: [.NET Pack](https://github.com/codebeltnet/dotnet-pack)\
:bookmark: [.NET Restore](https://github.com/codebeltnet/dotnet-restore)\
:bookmark: [.NET Test](https://github.com/codebeltnet/dotnet-test)\
:bookmark: [Install .NET SDK](https://github.com/codebeltnet/install-dotnet)\
:bookmark: [Install .NET Tool - MinVer](https://github.com/codebeltnet/dotnet-tool-install-minver)\
:bookmark: [Install .NET Tool - Report Generator](https://github.com/codebeltnet/dotnet-tool-install-reportgenerator)\
:bookmark: [Install .NET Tool - Sonar Scanner](https://github.com/codebeltnet/dotnet-tool-install-sonarscanner)\
:bookmark: [GCP Download File](https://github.com/codebeltnet/gcp-download-file)\
:bookmark: [Git Checkout](https://github.com/codebeltnet/git-checkout)\
:bookmark: [MinVer Calculate](https://github.com/codebeltnet/minver-calculate)\
:bookmark: [NuGet Push](https://github.com/codebeltnet/nuget-push)\
:bookmark: [Shell Globbing](https://github.com/codebeltnet/shell-globbing)\
:bookmark: [Analyze with SonarCloud](https://github.com/codebeltnet/sonarcloud-scan)\
:bookmark: [Finalyze with SonarCloud](https://github.com/codebeltnet/sonarcloud-scan-finalize)
