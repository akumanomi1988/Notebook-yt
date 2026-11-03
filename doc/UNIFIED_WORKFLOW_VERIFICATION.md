# Unified production workflow verification

## Verdict

The repository does not yet implement the requested unattended daily production system.
Successful uploads demonstrate the media and YouTube path, not automatic planning,
research, expert coordination, generation, infographic creation and reserve management.
Some previous operations were performed directly with the CLI and therefore did not
advance the application's production ledger. Those operations cannot be treated as
proof that the equivalent web workflow works.

## Implemented corrections

- `GET /api/studio/productions/{id}/workflow` exposes server-derived quality,
  expert-stage evidence, external operation IDs, readiness and remaining limitations.
  The production web screen uses this same endpoint.
- Video generation jobs require approved research, strategy, script and production
  expert results with persisted responses and conversation IDs. They also require an
  approved editorial package and at least 20 distinct ready NotebookLM sources.
- New upload requests require all seven expert stages, existing technical/editorial
  quality checks and publication approval. The worker checks the daily limit again
  before starting or resuming a resumable upload.
- Daily limits use `YouTubeUploadConfirmed` events, not video publication age, job
  creation time or verification time. Calendar boundaries use the channel timezone,
  including daylight-saving changes. A pending/ambiguous upload reserves the channel.
- Originals downloaded by the registered NotebookLM adapter are eligible for the
  same inspection, canonical selection and editorial review path as attested imports.
  Neither provenance mechanism constitutes cryptographic proof from Google.
- `GET /api/studio/productions/{id}/reserve.zip` exports a reviewed episode with
  `video.mp4`, its thumbnail, `youtube.json` and versioned source-package Markdown.
  The JSON contains publication metadata, source provenance, expert responses,
  references and file checksums. It excludes executable paths, upload sessions and
  credentials. Media and source-package hashes are checked before export. An
  incomplete episode or changed file returns 409 instead of a reserve package.
- The workflow view distinguishes an accepted generation request from a verified
  finished video. A successful CLI invocation is not audiovisual acceptance.

No schema change or operational database migration is required for these corrections.
Historical upload records remain idempotent: requesting an existing upload returns
the existing record rather than creating another upload.

## Remaining product gaps

| Requirement | Actual implementation |
| --- | --- |
| Seven new researched topics per day | Scheduler assigns already approved topics within its horizon; it does not discover or validate seven new topics daily. |
| One unattended production pipeline | Expert workflows and NotebookLM operations are separate durable queues; a coordinator does not yet connect all phases. |
| Research import and coverage | Research initiation and status are exposed, but discovered sources are not automatically approved/imported as a complete evidence package. |
| Generation tracking and automatic download | Generation request IDs are stored; accepted requests are not automatically polled and downloaded to completion. |
| NotebookLM infographic thumbnail | No infographic generation/download method exists in `INotebookLmProvider`. The existing thumbnail extractor uses a video frame. |
| Expert SEO output applied to metadata | Expert responses are stored, but there is no schema-validated application of SEO/title/description fields to a production revision. |
| Reusable expert conversation isolation | Current orchestrator copies expert notebooks per production. The requested reusable notebook strategy still needs verified conversation isolation and resource limits. |
| Automatic reserve accumulation | Reviewed packages can now be exported, but daily production/retention of a stockpile is not yet orchestrated. |

These are implementation gaps, not licensing conclusions or claims of exhausted quota.

## Agent contract

The web and an agent must use the same local API and production IDs. Agents must
enqueue expert workflows and NotebookLM operations through Notebook-yt, inspect
their persisted results, and use the normal review/upload endpoints. They must not
invoke the CLI, copy downloaded media into internal storage or call YouTube directly
to bypass application state. An agent with independent OS/provider access cannot
be technically constrained by the application once it chooses to operate outside it.

Do not schedule the proposed full daily automation as operationally complete until
the missing coordinator and provider methods above are implemented and exercised.

## Verification environment

Tests use an isolated PostgreSQL cluster on loopback port 15436 and an isolated
packaged application on port 15082. The operator database, runtime on port 5080,
NotebookLM resources and YouTube videos are not modified by this verification.

Commands:

```powershell
& "$env:LOCALAPPDATA\NotebookYt\dotnet\dotnet.exe" build src/NotebookYt.Api --nologo --no-restore
& "$env:LOCALAPPDATA\NotebookYt\dotnet\dotnet.exe" test tests/NotebookYt.Tests --nologo --logger 'console;verbosity=minimal'
npm --prefix web run typecheck
npm --prefix web test -- --reporter=dot --maxWorkers=1 --pool=threads
npm --prefix web run build
npm --prefix web run test:e2e
git diff --check
```

Set `NOTEBOOK_TEST_POSTGRES`, `TEST_PG_BIN`, `TEST_FFMPEG` and `TEST_FFPROBE` to
isolated local test dependencies; set `PLAYWRIGHT_BASE_URL` to the isolated app.
Do not point these tests at the operator database.

Regression coverage includes DST boundaries, persisted completion dates, ambiguous
upload reservations, invalidated expert results, direct API bypass attempts,
incomplete reserve rejection, ZIP contents and checksums, post-review corruption,
recoverable UI errors and the shared browser/API gate.

All external provider results in automated tests are controlled fixtures. No new
live NotebookLM generation or YouTube upload is claimed by this change.

Final executed results (2026-11-03): backend build succeeded; 121 backend tests
passed with exit code 0, including PostgreSQL migrations and isolated backup/restore;
24 frontend tests passed; TypeScript and production frontend build succeeded;
7 Playwright E2E tests passed. The final backend run used an isolated runsettings
file with collection parallelism disabled and PostgreSQL command timeout 120 seconds.
An earlier parallel run passed its assertions but failed fixture cleanup under load;
it is not counted as a successful suite execution. The first frontend parallel run
also timed out worker startup; the successful run used one threads worker.

Changes are not installed in the operator runtime on port 5080. They are a tested
increment on a separate branch, not acceptance of the full unattended product.
