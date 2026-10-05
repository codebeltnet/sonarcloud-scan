#!/usr/bin/env bash
# Preserve the default mode's Bash prerequisite, including Bash 3.2 on macOS.
set -euo pipefail

fail() {
  printf '%s\n' "::error::$1" >&2
  exit 1
}

case "$INPUT_COVERAGE_MODE" in
  opencover|normalized) ;;
  *) fail "Unsupported coverage-mode. Use 'opencover' or 'normalized'." ;;
esac

# Normalized mode validates its full contract through the PowerShell wrapper.
if [[ "$INPUT_COVERAGE_MODE" == normalized ]]; then
  [[ "${1:-}" == Validate ]] || fail 'OpenCover scanner invocation requires coverage-mode=opencover.'
  command -v pwsh >/dev/null || fail 'Normalized coverage requires caller-installed PowerShell 7 and a stable .NET 10 SDK.'
  exit 0
fi

# Parse quoted arguments literally. Never use eval, expansion or executable input.
parameters=()
text="${INPUT_PARAMETERS:-}"
token=''
quote=''
started=false
i=0
while (( i < ${#text} )); do
  character="${text:i:1}"
  if [[ "$character" == "\\" && "$quote" != "'" ]] && (( i + 1 < ${#text} )); then
    next="${text:i+1:1}"
    if { [[ -z "$quote" ]] && [[ "$next" == [[:space:]] || "$next" == "'" || "$next" == '"' || "$next" == "\\" ]]; } ||
       { [[ "$quote" == '"' ]] && [[ "$next" == '"' || "$next" == "\\" ]]; }; then
      token+="$next"
      started=true
      (( i += 2 ))
      continue
    fi
  fi
  if [[ -n "$quote" ]]; then
    if [[ "$character" == "$quote" ]]; then quote=''; else token+="$character"; fi
  elif [[ "$character" == "'" || "$character" == '"' ]]; then
    quote="$character"
    started=true
  elif [[ "$character" == [[:space:]] ]]; then
    if [[ "$started" == true ]]; then parameters+=("$token"); token=''; started=false; fi
  else
    token+="$character"
    started=true
  fi
  (( i += 1 ))
done
[[ -z "$quote" ]] || fail 'The parameters input has an unterminated quote. Supply quoted scanner arguments, without shell commands.'
if [[ "$started" == true ]]; then parameters+=("$token"); fi

shopt -s nocasematch
coverage_property='sonar\.(cs\.opencover\.reportsPaths|coverageReportPaths)[[:space:]]*='
# ${array[@]+...} also handles empty arrays under Bash 3.2's nounset behavior.
for parameter in ${parameters[@]+"${parameters[@]}"}; do
  if [[ "$parameter" =~ $coverage_property ]]; then
    fail 'The parameters input cannot set sonar.cs.opencover.reportsPaths or sonar.coverageReportPaths. Select coverage-mode; the action owns both coverage properties.'
  fi
done
shopt -u nocasematch

case "${1:-}" in
  Validate) exit 0 ;;
  Begin) ;;
  *) fail 'Use Validate or Begin for the OpenCover scanner phase.' ;;
esac

scanner_arguments=(
  sonarscanner begin
  "-k:$INPUT_PROJECT_KEY" "-o:$INPUT_ORGANIZATION" "-v:$INPUT_VERSION"
  "-d:sonar.token=$SONAR_TOKEN" "-d:sonar.host.url=$INPUT_HOST"
  "-d:sonar.cs.opencover.reportsPaths=$GITHUB_WORKSPACE/artifacts/TestResults*/**/*opencover*.xml"
  "-d:sonar.cs.vstest.reportsPaths=$GITHUB_WORKSPACE/artifacts/TestResults*/**/*.trx"
)
scanner_arguments+=(${parameters[@]+"${parameters[@]}"})
if dotnet "${scanner_arguments[@]}"; then
  exit 0
else
  code="$?"
  printf '%s\n' "::error::SonarScanner begin failed (exit $code). Review the upstream scanner output above." >&2
  exit "$code"
fi
