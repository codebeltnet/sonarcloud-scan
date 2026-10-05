[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$scanner = Join-Path $repository 'scripts/Scanner.ps1'
$realDotnet = (Get-Command dotnet -CommandType Application | Select-Object -First 1).Source
$contractState = [pscustomobject] @{ Captured = @(); Failure = '' }
$passed = 0
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('sonar-contract-' + [Guid]::NewGuid().ToString('N'))
[void] [IO.Directory]::CreateDirectory($temporary)
$variables = @('GITHUB_WORKSPACE', 'GITHUB_ACTION_PATH', 'INPUT_COVERAGE_MODE', 'INPUT_COVERAGE_SOURCE_ROOT',
    'INPUT_COVERAGE_PARTITION_REGEX', 'INPUT_COVERAGE_MAX_IDENTITY_SPREAD', 'INPUT_PARAMETERS',
    'INPUT_PROJECT_KEY', 'INPUT_ORGANIZATION', 'INPUT_VERSION', 'INPUT_HOST', 'SONAR_TOKEN')
$saved = @{}
foreach ($name in $variables) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }

# Only the external scanner is replaced. Normalization and reconciliation run the real CLI.
function dotnet {
    if ($args[0] -eq 'sonarscanner') {
        $contractState.Captured = @($args)
        $global:LASTEXITCODE = $(if ($contractState.Failure -eq 'scanner') { 7 } else { 0 })
        return
    }
    if ($contractState.Failure -eq 'sdk' -and $args[0] -eq '--list-sdks') {
        '9.0.100 [/sdk]'
        $global:LASTEXITCODE = 0
        return
    }
    if ($contractState.Failure -eq 'missing-output' -and $args -contains '--file-list') { $global:LASTEXITCODE = 0; return }
    & $realDotnet @args
    $global:LASTEXITCODE = $LASTEXITCODE
}
function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}
function Assert-Fails([scriptblock] $Action, [string] $Message) {
    $failed = $false
    try { & $Action 6>$null } catch { $failed = $_.Exception.Message -like "*$Message*" }
    Assert-True $failed "Expected failure containing '$Message'."
}
function Reset-Case([string] $Name, [string] $Mode = 'normalized') {
    $env:GITHUB_WORKSPACE = Join-Path $temporary $Name
    [void] [IO.Directory]::CreateDirectory($env:GITHUB_WORKSPACE)
    $env:GITHUB_ACTION_PATH = $repository
    $env:INPUT_COVERAGE_MODE = $Mode
    $env:INPUT_COVERAGE_SOURCE_ROOT = 'src'
    $env:INPUT_COVERAGE_PARTITION_REGEX = '^([^/]+?)(?:-[0-9a-f]{16,})?/([^/]+)/'
    $env:INPUT_COVERAGE_MAX_IDENTITY_SPREAD = ''
    $env:INPUT_PARAMETERS = "-d:sonar.exclusions='**/obj/**,**/bin/**'"
    $env:INPUT_PROJECT_KEY = 'Project'
    $env:INPUT_ORGANIZATION = 'Organization'
    $env:INPUT_VERSION = '1.0.0'
    $env:INPUT_HOST = 'https://sonarcloud.io'
    $env:SONAR_TOKEN = 'fixture-token'
    $contractState.Captured = @()
    $contractState.Failure = ''
}
function Write-Evidence([string] $Variant = 'Debug', [int] $Line = 10, [int] $Visits = 1, [string] $Source = '/workspace/src/Product/A.cs', [string] $FileId = '1') {
    $path = Join-Path $env:GITHUB_WORKSPACE "artifacts/TestResults-$Variant-Linux-X64/net10.0/project/coverage.opencover.xml"
    [void] [IO.Directory]::CreateDirectory((Split-Path $path -Parent))
    $escaped = [Security.SecurityElement]::Escape($Source)
    $xml = @"
<CoverageSession><Modules><Module><Files><File uid="1" fullPath="$escaped"/></Files><Classes><Class><Methods><Method><Name>System.Void Product.A::M()</Name><SequencePoints><SequencePoint sl="10" vc="$Visits" fileid="$FileId"/></SequencePoints><BranchPoints><BranchPoint sl="$Line" vc="$Visits" fileid="$FileId" ordinal="0" path="0"/></BranchPoints></Method></Methods></Class></Classes></Module></Modules></CoverageSession>
"@
    [IO.File]::WriteAllText($path, $xml, [Text.UTF8Encoding]::new($false))
    return $path
}
function Test-Case([string] $Name, [scriptblock] $Action) {
    & $Action
    $script:passed++
    Write-Host "PASS: $Name"
}
try {
    Test-Case 'metadata defaults and download/normalize/begin order' {
        $action = Get-Content (Join-Path $repository 'action.yml') -Raw
        Assert-True ($action -match '(?s)  coverage-mode:.*?default: opencover') 'Default mode changed.'
        Assert-True ($action -match '(?s)  coverage-source-root:.*?default: src') 'Default source root changed.'
        Assert-True ($action -match "(?s)  coverage-max-identity-spread:.*?default: ''") 'Guard acquired a default.'
        Assert-True ($action -match '(?s)Validate coverage contract.*Download all artifacts.*Normalize Sonar coverage.*SonarScanner for .NET') 'Action order changed.'
        Assert-True (([regex]::Matches($action, 'uses: actions/download-artifact@')).Count -eq 1) 'Downloads are duplicated.'
        Assert-True ($action -match 'actions/download-artifact@[0-9a-f]{40}') 'Download action is not pinned.'
        Assert-True ($action -match 'pattern: TestResults\*') 'Raw artifact pattern changed.'
        foreach ($inputName in @('MODE', 'SOURCE_ROOT', 'PARTITION_REGEX', 'MAX_IDENTITY_SPREAD')) {
            $expectedCount = $(if ($inputName -eq 'MODE') { 5 } else { 3 })
            Assert-True (([regex]::Matches($action, "INPUT_COVERAGE_${inputName}:")).Count -eq $expectedCount) "Input $inputName is not wired to all phases."
        }
    }
    Test-Case 'default OpenCover retains exact old arguments without requiring evidence or .NET 10' {
        Reset-Case 'raw workspace with spaces' 'opencover'
        $contractState.Failure = 'sdk'
        & $scanner -Phase Validate
        & $scanner -Phase Begin
        Assert-True ($contractState.Captured -contains "-d:sonar.cs.opencover.reportsPaths=$env:GITHUB_WORKSPACE/artifacts/TestResults*/**/*opencover*.xml") 'Raw glob changed.'
        Assert-True ($contractState.Captured -contains "-d:sonar.cs.vstest.reportsPaths=$env:GITHUB_WORKSPACE/artifacts/TestResults*/**/*.trx") 'VSTest glob changed.'
        Assert-True ($contractState.Captured -contains '-d:sonar.exclusions=**/obj/**,**/bin/**') 'Existing quoted defaults changed.'
        Assert-True (@($contractState.Captured | Where-Object { $_ -like '*sonar.coverageReportPaths=*' }).Count -eq 0) 'Generic property leaked into raw mode.'
    }
    Test-Case 'normalized fixture unions evidence and reconciles one generic report with no raw property' {
        Reset-Case 'normalized workspace with spaces'
        $rawA = Write-Evidence -Visits 0
        $rawB = Write-Evidence -Variant Release -Visits 1 -Source 'D:\agent\repo\src\Product\A.cs'
        $hashes = @((Get-FileHash $rawA).Hash, (Get-FileHash $rawB).Hash)
        & $scanner -Phase Validate
        & $scanner -Phase Normalize
        & $scanner -Phase Begin
        $report = Join-Path $env:GITHUB_WORKSPACE 'artifacts/CoverageNormalized/SonarQube.report.xml'
        Assert-True ($contractState.Captured -contains "-d:sonar.coverageReportPaths=$report") 'Generic report argument missing.'
        Assert-True (@($contractState.Captured | Where-Object { $_ -like '*sonar.cs.opencover.reportsPaths=*' }).Count -eq 0) 'Raw property leaked into normalized mode.'
        Assert-True ($contractState.Captured -contains "-d:sonar.cs.vstest.reportsPaths=$env:GITHUB_WORKSPACE/artifacts/TestResults*/**/*.trx") 'VSTest property missing.'
        [xml] $xml = Get-Content $report -Raw
        Assert-True ($xml.coverage.version -eq '1' -and $xml.coverage.file.path -eq 'src/Product/A.cs') 'Generic v1 source mapping wrong.'
        Assert-True ($xml.coverage.file.lineToCover.covered -eq 'true' -and $xml.coverage.file.lineToCover.branchesToCover -eq '1' -and $xml.coverage.file.lineToCover.coveredBranches -eq '1') 'Union counters wrong.'
        Assert-True ((Get-FileHash $rawA).Hash -eq $hashes[0] -and (Get-FileHash $rawB).Hash -eq $hashes[1]) 'Raw evidence changed.'
        Assert-True (@(Get-ChildItem (Split-Path $report) -Filter '*.xml').Count -eq 1) 'Multiple generic reports produced.'
        $oracle = Join-Path $repository 'tooling/CoverageNormalizer.Tests/Verify-OpenCoverEvidence.ps1'
        & $oracle -Report $rawB -Tool (Join-Path $repository 'tooling/CoverageNormalizer/bin/Release/net10.0/CoverageNormalizer.dll') -OutputDirectory (Join-Path $temporary 'oracle')
    }
    Test-Case 'invalid mode fails' { Reset-Case 'invalid-mode' 'unknown'; Assert-Fails { & $scanner -Phase Validate } 'Unsupported coverage-mode' }
    Test-Case 'missing SDK fails before downloading evidence' { Reset-Case 'missing-sdk'; $contractState.Failure = 'sdk'; Assert-Fails { & $scanner -Phase Validate } 'stable .NET 10' }
    Test-Case 'missing artifacts fails' { Reset-Case 'no-artifacts'; Assert-Fails { & $scanner -Phase Normalize } 'No TestResults artifacts' }
    Test-Case 'missing reports fails' {
        Reset-Case 'no-reports'
        [void] [IO.Directory]::CreateDirectory((Join-Path $env:GITHUB_WORKSPACE 'artifacts/TestResults-Debug'))
        Assert-Fails { & $scanner -Phase Normalize } 'No OpenCover reports'
    }
    foreach ($mode in @('opencover', 'normalized')) {
        foreach ($property in @('sonar.coverageReportPaths', 'sonar.cs.opencover.reportsPaths')) {
            Test-Case "reject reserved $property in $mode" {
                Reset-Case 'conflict' $mode
                $env:INPUT_PARAMETERS = "/d:$property='other report.xml'"
                Assert-Fails { & $scanner -Phase Validate } 'action owns both coverage properties'
            }
        }
    }
    Test-Case 'parameters and credentials remain literal arguments' {
        Reset-Case 'literal' 'opencover'
        $env:INPUT_PARAMETERS = '-d:sonar.name="Project with spaces" -d:sonar.literal=''$(throw "executed"); `whoami`'''
        $env:INPUT_PROJECT_KEY = 'key''; throw "executed"'
        & $scanner -Phase Begin
        Assert-True ($contractState.Captured -contains '-d:sonar.name=Project with spaces') 'Quoted argument split.'
        Assert-True ($contractState.Captured -contains '-d:sonar.literal=$(throw "executed"); `whoami`') 'Shell text evaluated or altered.'
        Assert-True ($contractState.Captured -contains "-k:$env:INPUT_PROJECT_KEY") 'Project key was evaluated or split.'
    }
    Test-Case 'unterminated parameter quote fails' { Reset-Case 'quote'; $env:INPUT_PARAMETERS = '-d:sonar.name="bad'; Assert-Fails { & $scanner -Phase Validate } 'unterminated quote' }
    Test-Case 'source-root flows into parser and summary' {
        Reset-Case 'custom-root'
        $env:INPUT_COVERAGE_SOURCE_ROOT = 'packages/source'
        $null = Write-Evidence -Source '/Users/runner/repo/packages/source/Product/A.cs'
        & $scanner -Phase Normalize
        [xml] $xml = Get-Content (Join-Path $env:GITHUB_WORKSPACE 'artifacts/CoverageNormalized/SonarQube.report.xml') -Raw
        $summary = Get-Content (Join-Path $env:GITHUB_WORKSPACE 'artifacts/CoverageNormalized/summary.json') -Raw | ConvertFrom-Json
        Assert-True ($xml.coverage.file.path -eq 'packages/source/Product/A.cs' -and $summary.productPrefix -eq 'packages/source/') 'Source root not passed.'
    }
    Test-Case 'spread guard is optional and caller-owned' {
        Reset-Case 'spread'
        $null = Write-Evidence
        $null = Write-Evidence -Variant Release -Line 45
        & $scanner -Phase Normalize
        $env:INPUT_COVERAGE_MAX_IDENTITY_SPREAD = '2'
        Assert-Fails { & $scanner -Phase Normalize } 'rejected the evidence'
        Assert-True (-not (Test-Path (Join-Path $env:GITHUB_WORKSPACE 'artifacts/CoverageNormalized/SonarQube.report.xml'))) 'Stale successful report survived rejection.'
    }
    Test-Case 'invalid guard fails' { Reset-Case 'guard'; $env:INPUT_COVERAGE_MAX_IDENTITY_SPREAD = '-1'; Assert-Fails { & $scanner -Phase Validate } 'nonnegative 32-bit integer' }
    Test-Case 'unset optional guard is treated as empty on every host' {
        Reset-Case 'unset-guard'
        [Environment]::SetEnvironmentVariable('INPUT_COVERAGE_MAX_IDENTITY_SPREAD', $null)
        & $scanner -Phase Validate
    }
    Test-Case 'invalid source root fails' { Reset-Case 'root'; $env:INPUT_COVERAGE_SOURCE_ROOT = '../src'; Assert-Fails { & $scanner -Phase Validate } 'repository-relative' }
    Test-Case 'custom partition pattern works and unmatched evidence fails' {
        Reset-Case 'partition'
        $null = Write-Evidence
        $env:INPUT_COVERAGE_PARTITION_REGEX = '^TestResults-(Debug)-Linux-X64/(net10\.0)/'
        & $scanner -Phase Normalize
        $env:INPUT_COVERAGE_PARTITION_REGEX = '^different/'
        Assert-Fails { & $scanner -Phase Normalize } 'does not match coverage-partition-regex'
    }
    Test-Case 'unresolved source identity fails' { Reset-Case 'parser'; $null = Write-Evidence -FileId 99; Assert-Fails { & $scanner -Phase Normalize } 'rejected the evidence' }
    Test-Case 'missing normalizer output fails' { Reset-Case 'no-output'; $null = Write-Evidence; $contractState.Failure = 'missing-output'; Assert-Fails { & $scanner -Phase Normalize } 'no nonempty generic' }
    Test-Case 'missing and empty report fail before scanner begin' {
        Reset-Case 'empty-report'
        Assert-Fails { & $scanner -Phase Begin } 'missing or empty'
        $path = Join-Path $env:GITHUB_WORKSPACE 'artifacts/CoverageNormalized/SonarQube.report.xml'
        [void] [IO.Directory]::CreateDirectory((Split-Path $path))
        [IO.File]::WriteAllText($path, '')
        Assert-Fails { & $scanner -Phase Begin } 'missing or empty'
    }
    Test-Case 'upstream scanner failure propagates' { Reset-Case 'scanner-failure' 'opencover'; $contractState.Failure = 'scanner'; Assert-Fails { & $scanner -Phase Begin } 'exit 7' }
    Write-Host "PASS: $passed action contract cases."
} finally {
    foreach ($name in $variables) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    $resolved = [IO.Path]::GetFullPath($temporary)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe temporary cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
