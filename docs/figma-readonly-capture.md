# Bounded Figma capture components

`Workbench.Testing.Domain.Sources.Figma` provides a backend read and manual source builder. This increment adds no native UI, secret setting, SDK capability, provider registry, discovery, or live-provider execution.

```csharp
FigmaReadRequest(string FileKey, IReadOnlyList<string> NodeIds, string? Version = null)
FigmaFileReader(HttpMessageHandler? handler = null)
Task<FigmaCapture> ReadAsync(FigmaReadRequest request,
    Func<CancellationToken, Task<string?>> getToken,
    CancellationToken cancellationToken = default)
TestSourceDraft FigmaSourceBuilder.CreateDraft(FigmaCapture capture,
    IReadOnlyDictionary<string, IReadOnlyList<string>> acceptanceCriteria)
```

The reader captures caller node lists synchronously before any credential work. File keys are 1–128 ASCII alphanumeric characters; 1–16 unique ordinal node IDs use basic decimal:decimal syntax and at most 64 characters. URLs and complex instance paths are unsupported. An optional version is 1–64 valid Unicode characters without whitespace or controls.

Each read issues at most one GET to `https://api.figma.com/v1/files/{key}/nodes?ids={ids}&depth=2`, with escaped values and an optional escaped `version`. Credentials are ASCII visible nonwhitespace strings of 1–4096 characters, sent only as the per-request `X-Figma-Token`. Invalid requests never evaluate the credential callback. The default per-call transport disables redirects, cookies and default credentials. An injected handler is caller-owned test transport; it can execute arbitrary code and is not a security sandbox. The reader disposes its client, response and default handler after actual work settles.

One cooperative 30-second linked deadline includes credential lookup, transport and body reads. Each operation is actually awaited: ignored cancellation keeps the returned task pending until that work settles. Caller cancellation becomes a sanitized `OperationCanceledException`; an elapsed deadline becomes `FigmaReadError.Timeout` after settlement. No retries, polling, URL traversal, images, comments, writes, Webhooks or automatic action inference occur.

Responses require `application/json`, optionally with UTF-8 charset, and no content encoding. Headers are read first; oversized Content-Length is refused before body reading. Streaming reads accept at most 1 MiB plus one overflow-detection byte. JSON has depth at most 32, strict UTF-8 and Unicode, and no duplicate properties at any object level, including unknown data. Bounded unknown API fields remain inert data. Required file name (1–200), version (1–64), requested nodes and document identities must all be present and valid; unavailable, null or extra nodes and version mismatches refuse the whole response. Node names are 1–200, types 1–64, and optional characters 0–4096. Nothing is truncated.

`FigmaCapture` exposes `Request`, `Name`, `Version`, `Nodes` and a defensive `OriginalJson` byte copy. Nodes preserve request order and contain only `Id`, `Name`, `Type`, `Characters`. Collections are read-only and caller inputs are copied. Credentials have no capture field.

The builder requires exactly the ordinal node keys in the capture, with 1–16 nonblank valid Unicode criteria per node, each at most 4096 characters and **at most 200 criteria in total**. Shared internal `FigmaInputCapture.CaptureRequest` and `CaptureCriteria` define synchronous admission for the reader, builder and subsequent controller integration. A controller must capture both inputs before calling the reader so 201 criteria refuse before credentials or HTTP. The standalone reader does not accept criteria.

Drafts use kind `Figma`, provider `figma.rest.nodes.manual-criteria`, provider version `1`, and document identity `fileKey@actualVersion`. Title, node locator and node title come from the observed response. Description labels observed type and characters and must fit 4096 characters or the builder refuses. Criteria are exact defensive copies of caller-supplied text: they are never claimed to have come from Figma. Every `VirtualScenario` is null and rule-generated steps remain manual. Without explicit criteria, no source can be built. Core source revisions cover both raw API bytes and supplied criteria; the raw-byte hash remains separately available.

`FigmaReadException.Error` is one of `InvalidRequest`, `CredentialsUnavailable`, `AuthenticationRefused`, `NotFound`, `RateLimited`, `Unavailable`, `ResponseTooLarge`, `ResponseInvalid`, `Timeout`. Its message is fixed and it has no upstream inner exception. 401/403 map to authentication refusal, 404 to not found, 429 to rate limited and all other failures/redirects to unavailable. Only a 429 integer Retry-After of 0–86400 seconds becomes `RetryAfterSeconds`. Error bodies, reason phrases, URLs and upstream exception text never enter an error or capture. Invalid caller input, including all criteria limits, maps to `InvalidRequest`; HTTP body overflow maps to `ResponseTooLarge`.

The existing combined generator limit of 200 cases and Core 4 MiB encoded-envelope limit remain authoritative. A legal Figma capture and criteria set can still fail Core publication with `StoreLimitExceeded`; there is no alternative serializer, truncation or cap increase. In-memory tests exercise request/credential boundaries, exact capture and manual provenance, malformed and oversized responses, cancellation settlement, the 200/201 boundary, revisions and safe final-envelope refusal. They perform no live Figma or provider calls. Full portable verification and Windows/native/human acceptance remain later integration gates.
