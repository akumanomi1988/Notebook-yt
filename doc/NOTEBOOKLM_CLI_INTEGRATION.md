# NotebookLM CLI Integration

## Scope and support status

Notebook-yt isolates the community-maintained `notebooklm-py` command-line client behind `INotebookLmProvider`. This is an unofficial integration that uses authenticated NotebookLM web behavior. It is not a Google-supported API and can break when Google changes the service. It does not bypass authentication, CAPTCHAs, account entitlements, quotas, or access controls. NotebookLM remains the sole Video Overview generator.

The reviewed dependency is `notebooklm-py[browser,cookies]==0.8.6` (release tag `v0.8.6`, commit `956400a`, reviewed 2026-10-31). The local adapter refuses a different CLI version. Install it into the operator's isolated local virtual environment with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Install-NotebookLmCli.ps1
```

The CLI profile lives in `%LOCALAPPDATA%\NotebookYt\operator\notebooklm-cli-profile`. Browser credentials are not copied to the repository or application database. Version 0.8.6 provides a top-level `notebooklm login` command that opens its own Chromium login window; `notebooklm auth login` does not exist. The login window requires the operator to authenticate directly with Google. The alternate browser-cookie path could not read Edge while its database was locked, and Notebook-yt will not export cookies, use another extraction route, or print them.

## Implemented local behavior

- Read-only diagnostics for version, profiles, authentication and usage.
- Notebook metadata, source and artifact listing.
- Durable, idempotent operations for notebook creation, adding a URL source, deep/fast research, Explainer video generation and exact-artifact video download.
- Durable expert-to-notebook bindings, source-scoped chat, approved-context transfer, response/citation storage and human approval. Queries continue the notebook's existing chat; the adapter deliberately avoids `ask --new`, which notebooklm-py 0.8.6 documents as permanently deleting the current conversation and its turns.
- A per-account process gate, bounded process timeouts/output, a pinned executable, SSRF checks for source URLs, and no raw stderr/credentials in logs.
- Generation is not dispatched if the effective Video Overview availability cannot be confirmed. Deferred quota requests are rechecked periodically; ambiguous external outcomes are not automatically retried.
- Episode research and new episode sources must use that production's registered notebook. Expert notebooks are protected as persistent knowledge stores.
- Downloaded artifacts are linked to the exact provider artifact ID, checksummed and queued for FFprobe/FFmpeg inspection. The application does not label that association cryptographic proof and does not bypass the existing operator approval gates.

## Commands used for the 2026-10-31 account diagnosis

The executable used was the operator-local venv's `notebooklm.exe`; it reported `0.8.6 (956400aa)`.

```powershell
notebooklm --version
notebooklm profile list --json
notebooklm auth inspect --browser edge --json
notebooklm auth check --test --json
notebooklm usage --json
notebooklm list --json
```

Observed results: no authenticated CLI profile was present in the isolated profile directory. `auth inspect` could not read Edge cookies because the browser database was locked. `auth check`, `usage`, and `list` returned `AUTH_REQUIRED`. The official `notebooklm login --profile operator --browser chromium` flow was then opened for five minutes, but the CLI did not detect a completed Google sign-in and exited with `Login not detected within 5 minutes`. Therefore the account identity, the existing notebook's remote IDs and sources, the current account's actual usage, and a quota reset time remain unverified through the CLI. This is evidence of an unavailable local CLI session, not evidence that the plan quota is exhausted or that Google AI Ultra is required.

Before integration, the existing application used its Enterprise API path for remote notebook operations while the operator's existing generation/download workflow was manually attested. The repository cannot by itself prove why a prior agent stopped at that point. The CLI session failure is the current reproduced provider blocker.

## Existing resource reconciliation

The operator-local PostgreSQL records and the authorized YouTube read were reconciled. The private reconciliation record is stored outside Git in the operator profile. Prior local evidence records two of the existing episode sources processed in the official NotebookLM UI; the third was rejected, and the remote source IDs remain unknown until the CLI can authenticate. Do not infer those missing IDs or add replacement sources without inspecting the authenticated notebook.

The local Notebook-yt artifact and the single stored upload record correspond to one remote NewSphere video by upload-to-artifact association, matching byte size and media properties. A live read from YouTube Personal Manager verified the configured channel, public visibility, successful processing and current remote title. These are strong reconciliation signals, but they are not cryptographic proof that Google generated the MP4. The local production status and an older approved metadata snapshot have not yet been synchronized to the live remote observation; use the application's authenticated verification action to record that observation without rewriting the original snapshot.

The CLI did not access or synchronize the notebook during this audit. No new notebook, source, research task or video was created; no video was downloaded or uploaded.

## References

- [notebooklm-py release history](https://github.com/teng-lin/notebooklm-py/releases)
- [notebooklm-py documentation and CLI reference](https://github.com/teng-lin/notebooklm-py)
- [NotebookLM official help: usage limits](https://support.google.com/gemini/answer/16213268)
- [NotebookLM official help: generate and customize Video Overviews](https://support.google.com/gemininotebook/answer/16454555)
