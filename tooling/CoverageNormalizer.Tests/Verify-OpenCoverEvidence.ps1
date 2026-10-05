[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Report,
    [Parameter(Mandatory)] [string] $Tool,
    [Parameter(Mandatory)] [string] $OutputDirectory
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$xmlPath = Join-Path $OutputDirectory 'SonarQube.report.xml'
$jsonPath = Join-Path $OutputDirectory 'summary.json'
$csvPath = Join-Path $OutputDirectory 'branch-identities.csv'
$list = Join-Path $OutputDirectory 'single-report.txt'
[IO.File]::WriteAllText($list, (Resolve-Path $Report).Path, [Text.UTF8Encoding]::new($false))
& dotnet $Tool --file-list $list --out $xmlPath --summary-json $jsonPath --diag-csv $csvPath
if ($LASTEXITCODE -ne 0) { throw "Single-report normalizer failed: $LASTEXITCODE" }

# Independent DOM oracle, deliberately not the streaming implementation.
[xml] $raw = Get-Content $Report -Raw
$modules = @($raw.SelectNodes('/CoverageSession/Modules/Module'))
$files = 0; $methods = 0; $sequence = 0; $branches = 0; $skipped = 0
$lineObservations = 0; $branchObservations = 0
$lines = @{}; $identities = @{}
function Canonical([string] $path) {
    $p = $path.Replace('\', '/')
    if ($p -match '(^|/)src/') { return 'src/' + $p.Substring($Matches[0].Length + $p.IndexOf($Matches[0], [StringComparison]::Ordinal)) }
    return $p.TrimStart('/')
}
foreach ($module in $modules) {
    $map = @{}
    foreach ($file in $module.SelectNodes('Files/File')) { $files++; $map[[string]$file.uid] = Canonical $file.fullPath }
    foreach ($method in $module.SelectNodes('Classes/Class/Methods/Method')) {
        $methods++
        $methodName = $method.SelectSingleNode('Name').InnerText
        foreach ($point in $method.SelectNodes('SequencePoints/SequencePoint | BranchPoints/BranchPoint')) {
            $isBranch = $point.Name -eq 'BranchPoint'
            if ($isBranch) { $branches++ } else { $sequence++ }
            $line = [int]$point.sl
            if ($line -le 0 -or $line -eq 0xFEEFEE) { $skipped++; continue }
            $file = $map[[string]$point.fileid]
            if (-not $file) { throw "Oracle unresolved fileid $($point.fileid)" }
            $covered = [long]$point.vc -gt 0
            if (-not $isBranch) {
                $lineObservations++
                $key = "$file|$line"
                $lines[$key] = $covered -or $lines[$key]
                continue
            }
            $branchObservations++
            $key = "$file|$methodName|$($point.ordinal)|$($point.path)"
            if (-not $identities.ContainsKey($key)) {
                $identities[$key] = @{ Min = $line; Max = $line; Observations = 0; Covered = $false }
            }
            $id = $identities[$key]
            $id.Min = [Math]::Min($id.Min, $line); $id.Max = [Math]::Max($id.Max, $line)
            $id.Observations++; $id.Covered = $id.Covered -or $covered
        }
    }
}
$summary = Get-Content $jsonPath -Raw | ConvertFrom-Json
$expected = @{ modules = $modules.Count; files = $files; methods = $methods; rawSequencePoints = $sequence; rawBranchPoints = $branches; skippedPoints = $skipped; lineObservations = $lineObservations; branchObservations = $branchObservations; unmatchedFileIds = 0; reportsParsed = 1 }
foreach ($key in $expected.Keys) {
    if ($summary.$key -ne $expected[$key]) { throw "Parser fidelity $key expected=$($expected[$key]) actual=$($summary.$key)" }
}
if ($summary.all.sequenceLinesTotal -ne $lines.Count) { throw 'Sequence denominator mismatch' }
if ($summary.all.sequenceLinesCovered -ne @($lines.Values | Where-Object { $_ }).Count) { throw 'Sequence coverage mismatch' }
[xml] $emitted = Get-Content $xmlPath -Raw
$emittedLines = @{}
foreach ($file in $emitted.coverage.file) {
    foreach ($line in $file.lineToCover) { $emittedLines["$($file.path)|$($line.lineNumber)"] = [bool]::Parse($line.covered) }
}
foreach ($key in $lines.Keys) {
    if (-not $emittedLines.ContainsKey($key) -or $emittedLines[$key] -ne $lines[$key]) { throw "Sequence line fidelity mismatch: $key" }
}
$actual = @(Import-Csv $csvPath)
if ($actual.Count -ne $identities.Count) { throw 'Logical identity count mismatch' }
foreach ($row in $actual) {
    $key = "$($row.File)|$($row.Method)|$($row.Ordinal)|$($row.Path)"
    $id = $identities[$key]
    if (-not $id -or [int]$row.MinLine -ne $id.Min -or [int]$row.MaxLine -ne $id.Max -or [int]$row.Observations -ne $id.Observations -or [bool]::Parse($row.Covered) -ne $id.Covered) { throw "Identity fidelity mismatch: $key" }
}
& dotnet $Tool --verify-artifacts $xmlPath $jsonPath
if ($LASTEXITCODE -ne 0) { throw "Persisted artifact mismatch: $LASTEXITCODE" }
Write-Host "PASS: independent fixture fidelity modules=$($modules.Count) files=$files methods=$methods sequence=$sequence branches=$branches identities=$($identities.Count)"
