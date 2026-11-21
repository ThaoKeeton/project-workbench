# Owned Probe Output Regression Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox syntax for tracking.

**Goal:** Diagnose and locally repair the cross-platform owned-child stdout assertion without weakening the actual reporter.
**Architecture:** Real child process byte-level reproduction, exact transport/expectation correction at the proven boundary, preserved reporter failure/identity guards.
**Tech Stack:** PowerShell7.6/.NET10 on local Linux.
**Spec:** docs/superpowers/specs/2026-11-08-owned-output-repair-design.md

## Global Constraints

- Task implementer stays local: no GitHub writes/Actions/uploads. Parent user renewed public standard-runner Actions authorization; controller alone will coordinate reviewed-source CI after cost-conscious retention changes. No billing changes/purchases, real providers/credentials/Webhook/DingTalk/user-PC.
- Preserve existing data, source/evidence/failed reports, business repositories and old artifacts; no deletions/403 bypass/PR retries.
- This is a new script-regression task, not a sixth speculative native CLR fix. The older native27/9of27 CLR failure/five-round cap, shipping Windows/business/DPAPI/human gates stay open.
- Do not normalize or weaken the production reporter just to pass a host-newline assertion. Exact stdout preservation, original failure, PID+birth+exit, finite stdout/stderr bounds, JSON control-character safety and actual child completion must still be asserted.
- Existing dedicated feature checkout, one source writer; two untracked SourceAcceptance design/plan documents belong to the next stage and must not be committed or modified here.
- Full evidence is under .superpowers/sdd/owned-output-repair; use installed local tools/caches and write only permitted workspace/tmp/cache roots.

## Review Focus

- LF and CRLF stdout both preserve exact bytes/text; a missing/altered output must still fail the real assertion.
- Successful exit0 and original nonzero37 retain their distinct outcomes.
- Start-Process async redirection versus raw pipe copies cannot leave unjoined streams or change test byte assumptions.
- Exact16384/oversize/invalid/empty/missing streams and stdout65536 bounds retain actual reporter coverage.
- No Linux synthetic process test becomes a claim of real Windows/UIA/WinUI validation.

### Task 1: Diagnose and repair exact child output assertions

**Files:** inspect scripts/OwnedLabProbeOutput.psm1 and tests/scripts/OwnedLabProbeOutput.Tests.ps1. Product edits limited to these two files and a narrow additional tests/scripts regression only if needed; modify reporter only if evidence demonstrates its fault. No Core/WinUI/native/CI/provenance changes. Update docs/windows-verification.md only if necessary to explain exact evidence limits.

**Consumes:** Complete-OwnedLabProbeProcessOutput actual exported function, decoded CI Windows/Linux logs at /workspace/project-workbench-support/20261108-source-acceptance/single-action-test. Windows failure in Small asserted stdout, before the later scenarios ran.

**Produces:** exact and transport-stable output regression with recorded causal evidence. Preserve external reporter API/error codes/limits.

- [ ] 1. Read systematic-debugging and relevant TDD instructions; trace literal child newline bytes, actual parent redirected file bytes, actual emitted string and assertion expectation. Inspect recent58f56ef change and the original failed CI lines. Run the existing focused script as local baseline. Do not infer actual Windows byte mismatch from its assertion alone.
- [ ] 2. Before correction, construct a compiling/executable minimal local regression or deterministic input that exposes the identified mismatch, keeping production semantics. Preserve RED command/source/output and explain why it is the correct failure, not setup. If Windows alone is needed, document limitation and make only evidence-backed portable changes; do not speculate about native CLR.
- [ ] 3. Apply smallest proven correction. Pin literal LF and CRLF transport behavior using actual reporter output, compare exact strings and verify a genuinely missing or modified stdout fails. Keep successful/nonzero output and diagnostic framing/stream joining/identity guards. Use explicit byte fixtures or a single consistent raw stream transport if causal evidence supports it; no newline normalization inside the production reporter solely for these tests.
- [ ] 4. Run GREEN focused child-output regressions, then once the complete local PowerShell script regression set used by both CI smoke/debug/business/report steps. Read workflow commands and run every feasible local one; record exact failures/skips if any. No unrelated fullCore rerun is required for test-only edits that are outside its source graph; prove that exclusion. If production code is touched, run its actual affected graph once.
- [ ] 5. Parse changed PowerShell files; git diff --check; self-review exactBASE..HEAD range. Save argv/environment/raw logs/source snapshots/hash receipts and actual process assertions. Do not overwrite prior packets. Commit only task files with message "test: preserve exact owned child stdout across platforms". Local commit allowed; no push.
- [ ] 6. Write .superpowers/sdd/owned-output-repair/task-1-report.md with root cause versus inference, RED/GREEN evidence, changed files, full local checks, preserved behavior, actualWindows-not-run limitation, commit and writer release. Return only DONE/DONE_WITH_CONCERNS/BLOCKED/NEEDS_CONTEXT, commit, one-line tests and report path. No subagents; controller performs fresh spec+quality review.

## Self-review / preflight result

Single task: both the fixture and real reporter are examined together; tests remain exact rather than merely checking nonempty output. No next-stage interface changes. SourceAcceptance plan stays deferred until this task's review; no overlapping writer. Existing user no-question/autonomous authorization governs the routine repair.

