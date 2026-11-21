# Owned probe stdout assertions: local repair design

The authorized Windows run at source4e60f0e stopped in tests/scripts/OwnedLabProbeOutput.Tests.ps1 with "Completed stdout observations were lost." Linux passed the same script. The synthetic child writes explicit LF bytes, while assertions use Environment.NewLine and Small/Large/Empty modes use Start-Process redirection. That boundary is a hypothesis, not a proven Windows root cause.

Keep the real reporter's contract: preserve actual joined stdout bytes as decoded text, bounded stderr JSON before the original nonzero failure, exact process identity and confirmed exit. Compare possible repairs only after local byte-level reproduction: transport-stable synthetic fixtures/expectations, or production reporter correction if real bytes are lost. Do not normalize production output or drop exact stdout assertions to accommodate a test. Real Windows confirmation remains pending because only Linux is available and another Actions test is forbidden.

Implement a narrowly scoped, reviewable repair with red/green evidence that catches the identified newline/transport mismatch and still catches genuinely missing stdout. Preserve all existing 11 real-child behavior guards, bounds, diagnostic framing and first-failure behavior. No unrelated native helper/runtime work.

User has authorized autonomous local development, so routine design/implementation approval is not requested. Choice and evidence limits are coordinator decisions.

