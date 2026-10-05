# Coverage normalization validation

The normalizer and its original 44 tests were ported from the hardened Cuemon multi-dimensional coverage experiment:

- Repository: [codebeltnet/cuemon](https://github.com/codebeltnet/cuemon).
- Branch: `experiment/sonar-normalized-coverage-20261005`.
- Verified implementation: [`cc19d02643829c198e576887f230bc1053135605`](https://github.com/codebeltnet/cuemon/commit/cc19d02643829c198e576887f230bc1053135605).
- Successful Sonar acceptance run: [37308393043](https://github.com/codebeltnet/cuemon/actions/runs/37308393043).

This is historical validation evidence, not a runtime dependency. The shared implementation contains no repository-specific expected counters, project names, released revision or experiment exclusions. Source layout and partition identity are explicit contracts; any identity-spread acceptance bound belongs to the caller.

## Known compiler-divergence limitation

Compiler-divergent methods can make ordinal/path correlation ambiguous. The experiment's `ExceptionConverter.ParseJsonReader` demonstrated that matching ordinal/path pairs can be attributed to different decision locations across compilation variants. Comparing structural signatures and source-line spread exposes this ambiguity but cannot resolve compiler decision ownership from OpenCover evidence alone.

Normalization provides deterministic logical aggregation based on the available OpenCover evidence, not mathematically perfect compiler-independent branch identity. Do not assume all extra decisions are suffixes, that the union always equals the richest variant, or that a spread guard bounds every semantic mismerge. A caller that requires stronger identity proof needs additional source/compiler evidence or an explicit acceptance policy.

## Production scope and independent coverage views

No experiment-only coverage exclusions belong in production. Sonar retains its normal source universe and may count report-absent coverable lines as uncovered. The shared action verifies persisted report/summary consistency; it does not require production coverage to equal an experimental percentage.

Codecov continues consuming raw `TestResults*` execution reports through `jobs-codecov@v1` and `codecov-scan@v1`. Sonar consumes one derived generic report only when the caller opts in. These independent representations are deliberate.

## Cuemon rollout gate

Validate static metadata, the normalizer tests, action/workflow contracts, unchanged default OpenCover arguments and a controlled normalized fixture before publishing the approved backward-compatible additions through `sonarcloud-scan@v2` and `jobs-sonarcloud@v3`. Tag updates and publication require the repositories' normal approval step.

Only after both existing major tags expose the tested inputs, make a normal PR branch from Cuemon `main` with this addition to the Sonar job in `.github/workflows/verify.yml`:

```yaml
coverage-mode: normalized
coverage-source-root: src
# Cuemon acceptance guard from reviewed evidence; never a universal default.
coverage-max-identity-spread: '26'
```

Do not merge the disposable experiment branch. Keep the Linux/Windows matrices, Codecov, CodeQL, source universe and release/deploy behavior unchanged. PR scanner logs must show exactly one generic coverage parse and zero raw OpenCover parses. A successful full PR verification is required before proposing one v10.8.0 assurance replay to repair canonical Sonar main. The replay requires separate approval; no release publication is needed to test it.

## Future DLCGEAR migration

Move the normalizer model/parser/writer, tests, summary schema and provenance with their semantics intact. Rework hosting and invocation when DLCGEAR has a defined caller-managed runtime and artifact contract. Preserve raw evidence ownership, explicit source roots and compilation partitions, exclusive Sonar coverage configuration, persisted reconciliation and independent Codecov ingestion. Compiler-divergent identity correlation needs additional evidence rather than a new abstraction alone. No DLCGEAR runtime or architecture is introduced here.
