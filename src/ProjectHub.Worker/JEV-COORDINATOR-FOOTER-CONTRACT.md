# Coordinator-first JEV Footer Contract

This footer is for the implementer's routing turn after its structured implementation result. The Worker has already captured the implementation report and command exit codes. Do not edit files or run tools in this routing turn.

The first nonempty response line must be exactly one of:

```text
[NEXT : COORDINATOR]
[NEXT : JEV]
```

Use `[NEXT : COORDINATOR]` when the implementation report is ready for the read-only coordinator to review. Follow it with `[REPORT]` and a concise account of completed work, observed validation, remaining uncertainty, and any JEV result supplied by the Worker. The Worker forwards this report together with the structured implementation result and actual command evidence to the coordinator. This tag does not mean the whole task is complete; only the coordinator can return `[ACTION=END]` and the Worker still checks its own gates.

Use `[NEXT : JEV]` only when a semantic claim needs optional Judge AI verification before coordinator review. Follow it with `[VALIDATION REQUEST]` and one or more atomic questions. Do not include a report in this branch. Use only questions for which the Worker can supply evidence; a command merely mentioned in prose is not execution evidence. Each question needs a fixed PASS rule. Supported forms:

```text
[NEXT : JEV]
[VALIDATION REQUEST]
- NOUL | [HIGH] One falsifiable claim
  EVIDENCE: a short observed excerpt or a file in the working folder
  SCOPE: the current task
  COUNTEREXAMPLE: what would disprove the claim
  PASS: YES >= 0.80
- SCORE | A graded question
  1 = desired condition
  2 = minor problem
  3 = major problem
  PASS: SCORE <= 2.0
- CHOICE | A classification question
  EXPECTED = requested result
  OUT_OF_SCOPE = unrelated result
  PASS: EXPECTED
```

After a JEV response, the Worker will send its outcome back to this same implementer session in a read-only reporting turn. Then return `[NEXT : COORDINATOR]`, `[REPORT]`, and a concise report that distinguishes JEV PASS, PARTIAL, ERROR, and missing direct evidence. Do not change a threshold to force a pass. The Worker forwards the Judge outcome to the coordinator and never treats JEV PASS alone as task completion.

Use exactly one NEXT tag. Do not add Markdown fences around the response. If Judge AI is unavailable or its result is inconclusive, report that fact to the coordinator rather than claiming success.
