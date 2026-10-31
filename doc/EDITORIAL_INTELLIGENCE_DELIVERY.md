# Editorial intelligence delivery evidence

Verification date: 2026-10-31. Implementation branch: `feat/editorial-intelligence`. Base: `2ee9f61` (`feat/daily-operator-experience`). See [architecture, limitations and operator walkthrough](EDITORIAL_INTELLIGENCE.md).

## Results

| Verification | Executed result |
| --- | --- |
| .NET 10 backend build | Success, zero warnings/errors |
| Full xUnit suite with actual isolated PostgreSQL | 84 passed, zero failed/skipped |
| TypeScript check | Success |
| Frontend component/unit tests | 19 passed across six files |
| Production frontend build | Success; packaged bundle `index-CFV31VEU.js` |
| Authenticated Playwright E2E against packaged frontend and actual PostgreSQL | Six passed; rerun after restarting the final build on the same acceptance database |
| Migration down/up and pending-model check | Passed |
| Upgrade with legacy productions, seven niche rows and frozen source package | Passed without rewriting evidence |
| Configuration and custom archetype persistence across fresh application factories | Passed |
| Backup and isolated restoration, all mapped tables/media/keys/protected configuration | Passed in the full backend suite with controlled credentials/media |
| Mechanical UI detector | No findings (`[]`) |
| Desktop/mobile screenshots and keyboard smoke | Rendered and visually inspected; 390px layout has no horizontal page overflow |
| `git diff --check` | Success |

The screenshot evidence is local and ignored under `.impeccable/review/editorial-desktop.png` and `editorial-mobile.png`. It is test-fixture content, not a real completed episode. TRX evidence is ignored under `tests/NotebookYt.Tests/TestResults/editorial-intelligence.trx`. Controlled HTTP Google contracts and synthetic technical media are never represented as provider acceptance.

## Local operator installation

The existing private update mechanism was reused after verifying zero queued/running jobs. The old application was stopped; PostgreSQL, media, Data Protection keys, protected configuration and previous packaged binaries were backed up before migration. No password, token or credential value was printed or committed.

Installed URL: `http://127.0.0.1:5080/?profile=operator`. The existing desktop launcher and persistent profile remain in use. Local backup: `%LOCALAPPDATA%\NotebookYt\backups\editorial-20261031-095313`. Verified refreshes also created `%LOCALAPPDATA%\NotebookYt\backups\editorial-20261031-095943` and the final `%LOCALAPPDATA%\NotebookYt\backups\editorial-20261031-100401` after the migration. Deployment receipt: `%LOCALAPPDATA%\NotebookYt\operator\editorial-deployment.json`. The copied private updater is `Activate-EditorialIntelligence.ps1`; it uses the already tested packaged runtime from the isolated acceptance directory. These local profile files are outside Git.

Post-installation checks:

| Item | Before | After |
| --- | ---: | ---: |
| Productions | 9 | 9 |
| Weekday niches | 7 | 7 |
| Media artifacts | 3 | 3 |
| Source packages | 1 | 1 |
| YouTube upload records | 1 | 1 |
| Applied migrations | 3 | 4 |
| Queued/running jobs | 0 | 0 |

`/health` returned `{"status":"ok"}`. Unauthenticated access to `/api/studio/editorial/catalog` returned 401. The packaged JavaScript asset returned HTTP 200. Operator database port remains 55434, separate from test PostgreSQL on 55435. The tests logged in through the actual authenticated frontend on isolated port 5082; no operator password was requested or used in test fixtures. These checks do not claim a fresh manual login to the operator's own session.

The application running on 5080 is the verified local feature build. No merge to main or public deployment was performed. No Google account configuration, NotebookLM notebook, real generation, YouTube metadata, upload or visibility was changed by this implementation.

## Coverage added

The new suite covers inheritance/overrides, all eight base archetypes, persisted additional custom definitions, version conflicts, restricted NotebookLM combinations, Custom style, producer-note entitlement and actual review, lexical recommendation behavior, selected sources and source mixing, composed/editable outputs, snapshots and revision integrity, old-schema upgrade, restart persistence, detailed-review/publication blocking, fixed topic identity after freeze, API 404s, responsive Spanish controls, inherited states, failed-save recovery and actual browser package approval/export. Existing scheduling, authorization, media, worker recovery, idempotency, YouTube adapters and restoration tests were rerun.

The final browser suite uses a fresh authenticated acceptance instance. Test-only services are stopped after verification; operator services are left running. Test databases are preserved only as isolated evidence and are never substituted for the operator database.

## Commands and environment

The command sequence and required environment variables are in `EDITORIAL_INTELLIGENCE.md`. The final backend invocation used the installed .NET 10 SDK, `NOTEBOOK_TEST_POSTGRES` on loopback 55435, and explicit FFmpeg/FFprobe/PostgreSQL executable locations. The full test command was:

```powershell
dotnet test tests/NotebookYt.Tests --nologo -v:q --logger 'trx;LogFileName=editorial-intelligence.trx'
npm --prefix web run typecheck
npm --prefix web test -- --reporter=dot
npm --prefix web run build
# With PLAYWRIGHT_BASE_URL=http://127.0.0.1:5082 and TEST_FFMPEG set:
npm --prefix web run test:e2e -- --reporter=line
```

Failed intermediate attempts are disclosed in the implementation document. A preliminary read-only operator count query also failed because Windows argument handling removed SQL identifier quotes; the corrected query was passed through a private SQL file, and its before/after counts are shown above. No data-changing query was attempted in that failure.

## Files created

- `doc/EDITORIAL_INTELLIGENCE.md`
- `doc/EDITORIAL_INTELLIGENCE_DELIVERY.md`
- `src/NotebookYt.Api/Migrations/20261031083051_EditorialIntelligence.cs`
- `src/NotebookYt.Api/Migrations/20261031083051_EditorialIntelligence.Designer.cs`
- `src/NotebookYt.Api/Studio/EditorialIntelligence.cs`
- `src/NotebookYt.Api/Studio/EditorialWorkflow.cs`
- `src/NotebookYt.Api/Studio/EditorialEndpoints.cs`
- `tests/NotebookYt.Tests/EditorialIntelligenceTests.cs`
- `web/src/EditorialDirector.tsx`
- `web/src/EditorialDirector.test.tsx`
- `web/src/editorial-director.css`
- `web/e2e/editorial-intelligence.spec.ts`
- `web/e2e/editorial-support.ts`

## Files modified

- `.gitignore`
- `README.md`
- `src/NotebookYt.Api/EditorialDb.cs`
- `src/NotebookYt.Api/Program.cs`
- `src/NotebookYt.Api/Migrations/EditorialDbModelSnapshot.cs`
- `src/NotebookYt.Api/Studio/QualityGate.cs`
- `src/NotebookYt.Api/Studio/StudioEndpoints.cs`
- `src/NotebookYt.Api/Studio/StudioRules.cs`
- `tests/NotebookYt.Tests/BackupRestoreTests.cs`
- `tests/NotebookYt.Tests/ControlledMediaWorkflowTests.cs`
- `tests/NotebookYt.Tests/MigrationTests.cs`
- `tests/NotebookYt.Tests/StudioIntegrationTests.cs`
- `web/src/Studio.tsx`
- `web/e2e/studio.spec.ts`

## External verification boundary

Live official Google documentation was consulted. Generation and download remain official-interface manual operations. No newly configured account entitlement, actual Video Overview option application, genuine new export or real YouTube upload was exercised. Producer-note and actual-settings evidence in regression tests is explicitly controlled testimony. Full subjective factual review remains human work; local checks cannot certify original content, source truth, Google generation settings or YouTube monetization.
