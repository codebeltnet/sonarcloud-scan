[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('Validate', 'Normalize', 'Begin')] [string] $Phase
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Parse shell-style quoting for compatibility, but never evaluate shell code.
function Split-ScannerParameters([string] $Text) {
    $tokens = [Collections.Generic.List[string]]::new()
    $token = [Text.StringBuilder]::new()
    $quote = [char]0
    $started = $false
    for ($i = 0; $i -lt $Text.Length; $i++) {
        $character = $Text[$i]
        if ($character -eq '\' -and $quote -ne "'" -and $i + 1 -lt $Text.Length) {
            $next = $Text[$i + 1]
            if (($quote -eq [char]0 -and ([char]::IsWhiteSpace($next) -or $next -in @("'", '"', '\'))) -or
                ($quote -eq '"' -and $next -in @('"', '\'))) {
                [void] $token.Append($next)
                $i++
                $started = $true
                continue
            }
        }
        if ($quote -ne [char]0) {
            if ($character -eq $quote) { $quote = [char]0 }
            else { [void] $token.Append($character) }
            continue
        }
        if ($character -in @("'", '"')) { $quote = $character; $started = $true; continue }
        if ([char]::IsWhiteSpace($character)) {
            if ($started) { $tokens.Add($token.ToString()); [void] $token.Clear(); $started = $false }
            continue
        }
        [void] $token.Append($character)
        $started = $true
    }
    if ($quote -ne [char]0) { throw 'The parameters input has an unterminated quote. Supply quoted scanner arguments, without shell commands.' }
    if ($started) { $tokens.Add($token.ToString()) }
    return $tokens.ToArray()
}

try {
    $mode = $env:INPUT_COVERAGE_MODE
    if ($mode -cnotin @('opencover', 'normalized')) {
        throw "Unsupported coverage-mode '$mode'. Use 'opencover' or 'normalized'."
    }
    $parameters = @(Split-ScannerParameters $env:INPUT_PARAMETERS)
    foreach ($parameter in $parameters) {
        # Property ownership is exclusive, including duplicate same-mode overrides.
        if ($parameter -match '(?i)sonar\.(?:cs\.opencover\.reportsPaths|coverageReportPaths)\s*=') {
            throw 'The parameters input cannot set sonar.cs.opencover.reportsPaths or sonar.coverageReportPaths. Select coverage-mode; the action owns both coverage properties.'
        }
    }

    if ($mode -eq 'normalized') {
        $sourceRoot = ([string] $env:INPUT_COVERAGE_SOURCE_ROOT).Replace('\', '/').TrimEnd('/')
        if ([string]::IsNullOrWhiteSpace($sourceRoot) -or $sourceRoot.StartsWith('/') -or $sourceRoot.Contains(':') -or
            @($sourceRoot.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count -gt 0) {
            throw 'coverage-source-root must be a nonempty repository-relative directory without dot segments; for example, src.'
        }
        $partitionPattern = $env:INPUT_COVERAGE_PARTITION_REGEX
        if ([string]::IsNullOrWhiteSpace($partitionPattern)) { throw 'coverage-partition-regex must identify each build variant and target framework.' }
        $partitionRegex = [regex]::new($partitionPattern, [Text.RegularExpressions.RegexOptions]::CultureInvariant, [TimeSpan]::FromSeconds(5))
        $spread = [string] $env:INPUT_COVERAGE_MAX_IDENTITY_SPREAD
        $spreadValue = 0
        if ($spread -ne '' -and ($spread -notmatch '^\d+$' -or -not [int]::TryParse($spread, [ref] $spreadValue))) {
            throw 'coverage-max-identity-spread must be empty or a nonnegative 32-bit integer. This acceptance guard belongs to the caller.'
        }
        if ($Phase -eq 'Validate') {
            if ($null -eq (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Normalized coverage requires a caller-installed .NET 10 SDK. Install it before sonarcloud-scan.' }
            $sdks = @(& dotnet --list-sdks)
            if ($LASTEXITCODE -ne 0) { throw "Unable to list .NET SDKs (exit $LASTEXITCODE)." }
            if (@($sdks | Where-Object { $_ -match '^10\.0\.\d+\s+\[' }).Count -eq 0) {
                throw 'Normalized coverage requires a caller-installed stable .NET 10 SDK. Install it before sonarcloud-scan.'
            }
        }
    }
    if ($Phase -eq 'Validate') { return }

    $artifacts = Join-Path $env:GITHUB_WORKSPACE 'artifacts'
    $outputDirectory = Join-Path $artifacts 'CoverageNormalized'
    $report = Join-Path $outputDirectory 'SonarQube.report.xml'
    $summary = Join-Path $outputDirectory 'summary.json'
    if ($Phase -eq 'Normalize') {
        if ($mode -ne 'normalized') { throw 'Normalize requires coverage-mode=normalized.' }
        $evidence = @(Get-ChildItem -LiteralPath $artifacts -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -clike 'TestResults*' })
        if ($evidence.Count -eq 0) { throw 'No TestResults artifacts found. Upload raw TestResults* artifacts before running normalized coverage.' }
        $reports = @($evidence | ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Recurse -File | Where-Object { $_.Name -clike '*opencover*.xml' } } | Sort-Object FullName)
        if ($reports.Count -eq 0) { throw 'No OpenCover reports found in TestResults*. Upload **/*opencover*.xml as raw coverage evidence.' }
        foreach ($raw in $reports) {
            $relative = [IO.Path]::GetRelativePath($artifacts, $raw.FullName).Replace('\', '/')
            if (-not $partitionRegex.IsMatch($relative)) { throw "Report '$relative' does not match coverage-partition-regex. Include build variant and target framework in the partition contract." }
        }
        [void] [IO.Directory]::CreateDirectory($outputDirectory)
        # Never accept a stale report from a previous invocation.
        foreach ($derived in @($report, $summary)) {
            if (Test-Path -LiteralPath $derived) { Remove-Item -LiteralPath $derived }
        }
        # The file-list directory is the CLI partition root, matching downloaded artifacts.
        $list = Join-Path $artifacts '.coverage-normalizer-reports.txt'
        [IO.File]::WriteAllLines($list, [string[]] @($reports | ForEach-Object { [IO.Path]::GetRelativePath($artifacts, $_.FullName) }), [Text.UTF8Encoding]::new($false))
        $arguments = @('--file-list', $list, '--out', $report, '--summary-json', $summary,
            '--diag-csv', (Join-Path $outputDirectory 'branch-identities.csv'),
            '--divergence-csv', (Join-Path $outputDirectory 'divergent-methods.csv'),
            '--source-root', $sourceRoot, '--partition-regex', $partitionPattern, '--require-partition-match')
        if ($spread -ne '') { $arguments += @('--max-identity-spread', $spread) }
        $tooling = Join-Path $env:GITHUB_ACTION_PATH 'tooling'
        Push-Location $tooling
        try {
            & dotnet run --project (Join-Path $tooling 'CoverageNormalizer/CoverageNormalizer.csproj') -c Release -- @arguments
            if ($LASTEXITCODE -ne 0) { throw "CoverageNormalizer rejected the evidence (exit $LASTEXITCODE). Review the parser and identity diagnostics above and in '$outputDirectory'." }
            if (-not (Test-Path -LiteralPath $report -PathType Leaf) -or (Get-Item -LiteralPath $report).Length -eq 0) { throw 'CoverageNormalizer produced no nonempty generic coverage report.' }
            & dotnet run --project (Join-Path $tooling 'CoverageNormalizer/CoverageNormalizer.csproj') -c Release --no-build -- --verify-artifacts $report $summary
            if ($LASTEXITCODE -ne 0) { throw "Persisted coverage XML and summary failed reconciliation (exit $LASTEXITCODE)." }
        } finally { Pop-Location }
        return
    }

    $scannerArguments = @('sonarscanner', 'begin', "-k:$env:INPUT_PROJECT_KEY", "-o:$env:INPUT_ORGANIZATION",
        "-v:$env:INPUT_VERSION", "-d:sonar.token=$env:SONAR_TOKEN", "-d:sonar.host.url=$env:INPUT_HOST")
    if ($mode -eq 'normalized') {
        if (-not (Test-Path -LiteralPath $report -PathType Leaf) -or (Get-Item -LiteralPath $report).Length -eq 0) { throw 'The normalized generic coverage report is missing or empty. Normalization must succeed before scanner begin.' }
        $scannerArguments += "-d:sonar.coverageReportPaths=$report"
    } else {
        $scannerArguments += "-d:sonar.cs.opencover.reportsPaths=$env:GITHUB_WORKSPACE/artifacts/TestResults*/**/*opencover*.xml"
    }
    $scannerArguments += "-d:sonar.cs.vstest.reportsPaths=$env:GITHUB_WORKSPACE/artifacts/TestResults*/**/*.trx"
    $scannerArguments += $parameters
    & dotnet @scannerArguments
    if ($LASTEXITCODE -ne 0) { throw "SonarScanner begin failed (exit $LASTEXITCODE). Review the upstream scanner output above." }
} catch {
    $message = $_.Exception.Message.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
    Write-Host "::error::$message"
    throw
}
