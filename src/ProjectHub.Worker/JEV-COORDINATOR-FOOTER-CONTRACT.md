# Coordinator Router Footer

The configured coordinator owns interpretation of inbound message types and chooses the next action. Worker parses only the first control tag and optional destination; every following body is opaque.

Coordinator responses begin with exactly one of:

```text
[ACTION=CONTINUE]
[ACTION=PAUSE]
[ACTION=END]
[ACTION=HQ]
```

For `CONTINUE`, put one destination on the next non-empty line, then the body:

```text
[NEXT : IMPLEMENTER]
[NEXT : HIGH_LEVEL]
[NEXT : JUDGE]
[NEXT : COORDINATOR]
```

`ACTION=HQ` is shorthand for routing the opaque body to the currently configured coordinator role. The coordinator routine receives an envelope with `message_type` and `body`; it interprets whether the content is a user request, role report, judge result, or a technical error. Worker does not infer task meaning from the content.

Implementer and High-level role responses include their route in the same execution response. They return `[NEXT : COORDINATOR]` with a report, or `[NEXT : JUDGE]` with a validation request. Do not require a second route-only model call. The Worker forwards the body unchanged to the configured coordinator routine.

For JEV, the adapter parses the atomic question syntax needed to construct the HTTP request and returns the provider response unchanged. It does not turn scores into PASS/PARTIAL, downgrade results based on evidence availability, or decide what role runs next. The coordinator interprets the `JUDGE_RESULT` message type.

Worker retains transport and safety duties: selected role/session, workspace, process exit/cancel/timeout, HTTP status, transcript/usage, and unavailable-route reporting. It does not require a work-card schema, review JSON, validation command match, evidence freshness, fixed retry count, or semantic agreement before forwarding an AI response or accepting `[ACTION=END]`.
