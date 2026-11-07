# AI test authoring

The portable `Workbench.Testing.Domain.Generation.Ai.AiTestCaseGenerator` authors plain test cases for human review. It does not publish records or execute steps. Every `AiGeneratedStep.Action` is null, including when an input source originated in a mapped synthetic fixture.

```csharp
var generator = new AiTestCaseGenerator(handler: null);
AiGenerationCapture capture = await generator.GenerateAsync(
    sources, getKey, cancellationToken);
```

Construction performs no IO. `GenerateAsync` synchronously copies and validates its source data before invoking the supplied `Func<CancellationToken, Task<string?>>` once. Invalid caller input throws `AiGenerationException` with `InvalidRequest` synchronously. The callback supplies a key for that request's Authorization header; the client has no environment, file, settings or credential-store lookup. An injected `HttpMessageHandler` remains caller-owned.

The only request is `POST https://api.openai.com/v1/responses`, with model `gpt-5.4-mini-2026-03-17`, `reasoning.effort: none`, `store: false`, `stream: false`, `max_output_tokens: 8192` and strict `text.format` JSON schema `test_cases`. No tools, file/image inputs, previous responses or conversations are sent. Default transport disables redirects, cookies, default credentials and decompression. The client performs no automatic retry or URL traversal.

The input JSON preserves source, item and criterion order and literal text through normal JSON escaping. It contains source snapshot ID/revision, kind, provider ID/version, document ID/title, and each item's locator/title/description and indexed criteria. `OriginalBytes`, `OriginalSha256`, virtual scenarios and other source metadata are not submitted. Source IDs must be 32 lowercase hex characters and revisions 64. Accept 1–8 distinct sources, SDK-bounded source/item text and at most 32 criteria overall, with 1–16 criteria per item. Receipt provider `openai.responses.test-cases` cannot be an input. Exact UTF-8 input JSON is limited to 256 KiB; overflow is rejected without truncation or a credential lookup. After key lookup, a decoded input string/property containing that key is refused with `InvalidRequest` before HTTP; input is never redacted or truncated.

The generated JSON shape is exactly:

```json
{"cases":[{"criterionIndex":0,"title":"…","prerequisites":"","steps":[{"instruction":"…","expected":"…"}]}]}
```

Every property is required and extra properties are refused. There must be 1–32 cases, 1–16 steps per case and at most 256 steps overall. Each original criterion must be covered at least once. Indices refer to `capture.OriginalTraces[index]`, whose criterion index is local to the original item. Titles are 1–200 UTF-16 units; prerequisites 0–4096; instructions and expected results 1–4096. Required text must be nonblank and all text valid Unicode. Text is preserved without trimming.

Success requires the exact model snapshot, a completed response and one completed assistant message with one `output_text` part. Response IDs have 6–128 ASCII characters: `resp_` followed by letters, digits, `_` or `-`. Refusals, incomplete/error results and tool/action output are refused. Unknown bounded envelope fields remain inert data. Both JSON layers reject duplicate names, unpaired surrogates and depth above 32. Bodies require unencoded `application/json`, optionally with UTF-8 charset, and strict UTF-8. The body reader accepts at most 256 KiB and reads at most one extra byte to detect overflow. A response reflecting the supplied credential, including decoded escaped strings/property names, is rejected before capture construction.

A cooperative 90-second deadline covers the key callback, HTTP and body work. Cancellation and elapsed deadlines request cancellation, then await the actual callback, transport and stream work, including disposal. Dependencies that ignore cancellation can keep the operation pending beyond the deadline; no work is abandoned through timeout races. Caller cancellation becomes a fixed-message `OperationCanceledException` with the caller's token after settlement.

`AiGenerationException.Error` distinguishes invalid input, unavailable credentials, authentication refusal (401/403), rate limiting (429), unavailable transport/other HTTP failures, incomplete results, refusal, invalid or oversized responses, and timeout. Messages are fixed and retain no upstream exception or body. Only one integer `Retry-After` in 0–86400 seconds is retained for 429; callers decide whether to make a later explicit attempt.

`AiGenerationCapture` exposes defensive `InputJson`/`ResponseJson` byte copies, lowercase input SHA-256, observed model/response ID, read-only original traces and immutable generated cases/steps. It contains no credential field. AI output still needs human review: schema validity and trace coverage do not establish semantic correctness.

Verification uses synthetic credentials and in-memory HTTP handlers exclusively. No live provider request, account authorization, billing behavior or model availability has been verified. Publication and native controls are separate work.
