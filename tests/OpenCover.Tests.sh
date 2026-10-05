#!/usr/bin/env bash
set -euo pipefail
repository="$(cd "$(dirname "$0")/.." && pwd)"
scanner="$repository/scripts/OpenCover.sh"
temporary="$(mktemp -d)"
trap 'rm -f "$temporary/arguments"; rmdir "$temporary"' EXIT
export CAPTURE="$temporary/arguments"
export SCANNER_EXIT=0
dotnet() {
  printf '%s\0' "$@" > "$CAPTURE"
  return "$SCANNER_EXIT"
}
export -f dotnet
export INPUT_COVERAGE_MODE=opencover
export INPUT_PARAMETERS="-d:sonar.exclusions='**/obj/**,**/bin/**'"
export INPUT_PROJECT_KEY=Project INPUT_ORGANIZATION=Organization INPUT_VERSION=1.0.0
export INPUT_HOST=https://sonarcloud.io SONAR_TOKEN=fixture-token
export GITHUB_WORKSPACE='/workspace with spaces'
passed=0
assert_argument() {
  found=false
  while IFS= read -r -d '' argument; do
    if [[ "$argument" == "$1" ]]; then found=true; fi
  done < "$CAPTURE"
  [[ "$found" == true ]] || { printf '%s\n' "Missing argument: $1" >&2; exit 1; }
}
assert_failure() {
  if bash "$scanner" "$1" >/dev/null 2>&1; then
    printf '%s\n' 'Expected scanner contract failure.' >&2
    exit 1
  fi
}
bash "$scanner" Validate
[[ ! -f "$CAPTURE" ]] || { echo 'Validation invoked scanner.' >&2; exit 1; }
(( passed += 1 ))
bash "$scanner" Begin
assert_argument '-d:sonar.cs.opencover.reportsPaths=/workspace with spaces/artifacts/TestResults*/**/*opencover*.xml'
assert_argument '-d:sonar.cs.vstest.reportsPaths=/workspace with spaces/artifacts/TestResults*/**/*.trx'
assert_argument '-d:sonar.exclusions=**/obj/**,**/bin/**'
while IFS= read -r -d '' argument; do
  [[ "$argument" != *sonar.coverageReportPaths* ]] || { echo 'Generic coverage leaked into default mode.' >&2; exit 1; }
done < "$CAPTURE"
(( passed += 1 ))
# Single quotes are part of the parameter syntax, not executable shell text.
export INPUT_PARAMETERS="-d:sonar.name=\"Project with spaces\" -d:sonar.literal='\$(throw \"executed\"); \`whoami\`'"
export INPUT_PROJECT_KEY="key'; echo executed"
bash "$scanner" Begin
assert_argument '-d:sonar.name=Project with spaces'
# shellcheck disable=SC2016 # Literal shell text is the behavior under test.
assert_argument '-d:sonar.literal=$(throw "executed"); `whoami`'
assert_argument "-k:$INPUT_PROJECT_KEY"
(( passed += 1 ))
export INPUT_PARAMETERS=''
bash "$scanner" Begin
assert_argument '-d:sonar.cs.opencover.reportsPaths=/workspace with spaces/artifacts/TestResults*/**/*opencover*.xml'
(( passed += 1 ))
for property in sonar.coverageReportPaths sonar.cs.opencover.reportsPaths SONAR.COVERAGEREPORTPATHS; do
  export INPUT_PARAMETERS="/d:$property='other report.xml'"
  assert_failure Validate
  (( passed += 1 ))
done
export INPUT_PARAMETERS='-d:sonar.name="bad'
assert_failure Validate
(( passed += 1 ))
export INPUT_PARAMETERS=''
export INPUT_COVERAGE_MODE=unknown
assert_failure Validate
(( passed += 1 ))
export INPUT_COVERAGE_MODE=opencover
export GITHUB_WORKSPACE='D:\agent\workspace with spaces'
bash "$scanner" Begin
assert_argument '-d:sonar.cs.opencover.reportsPaths=D:\agent\workspace with spaces/artifacts/TestResults*/**/*opencover*.xml'
(( passed += 1 ))
export SCANNER_EXIT=7
if bash "$scanner" Begin >/dev/null 2>&1; then
  echo 'Scanner failure was swallowed.' >&2
  exit 1
else
  [[ "$?" == 7 ]] || { echo 'Scanner exit code changed.' >&2; exit 1; }
fi
(( passed += 1 ))
printf '%s\n' "PASS: $passed default OpenCover Bash contract cases."
