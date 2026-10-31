# Editorial intelligence and NotebookLM customization

## Delivery scope

This extension adds episode-level editorial direction to the existing local modular monolith. It preserves the operator authentication, seven weekday niches, calendar, media storage, worker, source provenance and YouTube approval workflow. It does not modify VideoNews AI Studio, generate audiovisual content, call a paid model or automate the NotebookLM website.

The installed implementation was initially `2ee9f61`, on `feat/daily-operator-experience`. Implementation work uses `feat/editorial-intelligence`. No branch history is rewritten and main is not merged automatically.

## Configuration and persistence

`EditorialConfiguration` has exactly one owner: channel, niche or production. Its check constraint and foreign keys enforce that scope. Writes require the current version; the existing PostgreSQL transaction/advisory-lock middleware serializes application writes, and the version is also an EF concurrency token.

Resolution is deterministic: existing channel/workspace values and product defaults, channel editorial overrides, existing niche preferences, niche editorial overrides, then production overrides. Empty explicit values remain explicit. The UI identifies inherited, archetype-suggested, workspace and episode values. Restoring inheritance removes an override rather than copying an inherited value into the episode.

`NarrativeDefinition` stores edits to stable presets and additional definitions. The catalog provides Investigation, Explainer, Historical Story, Comparison, Myths and Evidence, Case Study, Future Scenarios and Custom. Structures, purposes, visual guidance, weaknesses, templates and recommendation criteria are editable. New stable identifiers can be registered through the same versioned API; no schema or source-code change is necessary.

Approving the preview creates both a versioned `SourcePackage` and `EditorialPackageSnapshot`, with SHA256 integrity checks. The snapshot contains resolved fields, selected source records, archetype definitions, draft decisions, four composed outputs and the similarity findings. Edited outputs cannot remove the separate structured source/configuration manifest from exported packages. Manual prompt edits do not overwrite structured fields. Approval also aligns the existing workspace objective, audience and language so publishing does not retain a different language from the approved brief.

Approved snapshots are never updated through the API. A revision before any completed manual task or imported video creates another package version and retains the original evidence. Once work has been carried out, create another production. Substantive topic edits are rejected while an editorial snapshot is active. Editing drafts invalidates workspace publication approval. A stale preview hash rejects approval after a relevant context change.

Migration: `20261031083051_EditorialIntelligence`. Four additive tables: `EditorialConfiguration`, `EditorialPackageSnapshot`, `NarrativeDefinition`, `EditorialAssessment`. Existing production, media, calendar and source-package rows are not rewritten. Historical packages without a new editorial configuration retain their prior review contract; this is an explicit compatibility boundary, not a claim that they received the new detailed review. Historical frozen packages require another production to adopt a different editorial brief. Unpackaged legacy workspaces can adopt the new configuration by saving their editorial direction. New workspaces receive an editorial configuration automatically and cannot use the old package endpoint to bypass its approval.

## Documented capabilities versus account evidence

Official reference, retrieved 2026-10-31: [Generate Video Overviews](https://support.google.com/gemininotebook/answer/16454555?hl=en). [Google's Cinematic announcement](https://blog.google/innovation-and-ai/products/notebooklm/generate-your-own-cinematic-video-overviews-in-notebooklm/) supplies the additional Ultra entitlement context.

Explainer is the default. Short is explicit opt-in. Cinematic requires English and recorded operator evidence of eligible Ultra/18+ access. Style selection, including Custom, is limited to Explainer; Custom requires a description. The catalog lists documented language choices and restricts combinations absent from its Short catalog. Account availability is never inferred from documentation. Unknown availability stays unknown.

The producer-notes feature is gated by recorded availability and entitlement evidence. Requested notes are frozen with the package; actual reviewed notes, revision feedback and approval are subsequent timestamped events, so actual review does not rewrite the frozen request. These events are operator testimony, not provider verification.

Desired orientation and duration are local import requirements, not NotebookLM API parameters. The imported file's actual dimensions and duration are checked against them. The application does not offer fake resolution, frame-rate, narrator, TTS, model or exact-generation-duration controls.

Sources are selected from included records belonging to the production. The package preserves their identifiers and provenance. The operator must reconcile those identifiers with processed sources in the official notebook and explicitly attest the actual settings used. A mismatch is rejected. No source selection or generation is reported as remotely applied.

## Recommendations and differentiation

The recommendation engine is deterministic, optional guidance, not an AI or semantic-analysis claim. Editable keyword rules consider topic/question and available editorial metadata; source count, audience, prior knowledge, depth and recent archetype use contextualize its explanation and warnings. It provides an alternative, structure, angle, proposed contribution, visual direction and steering text. Accept, modify or reject decisions are persisted. A rejection/modification needs a rationale before approval.

Similarity uses normalized lexical tokens and Jaccard overlap across up to 100 other recent productions: topic/title, question, thesis, contribution, hook, structure and evidence plan. Frozen prior episodes use their effective snapshots. Findings identify overlapping fields, differences and the method. They do not establish factual duplication, search demand, monetization eligibility or causation. Similarity acknowledgement is tied to the current report fingerprint. A recurring series can proceed with an explicit rationale. Archetype frequency alone is advisory, never forced rotation.

The channel variety view displays episode/niche/stage, resolved styles and archetypes, distributions, repeated normalized questions and opportunities. No calendar assignment or date changes are made.

## Review and publication

The existing technical gate remains. For productions using the new contract, it additionally requires an intact approved package and a positive detailed editorial assessment tied to the current snapshot and canonical artifact. Each of ten semantic/subjective criteria needs human evidence. No automatic content PASS is generated. Partial/rejected evidence is persisted as a non-approval and can be resumed from the progress endpoint. Actual NotebookLM settings must have been attested for the current snapshot before a positive assessment.

Changing the canonical artifact prevents reuse of its predecessor's assessment. A later rejection invalidates workspace approval. The original final review and YouTube enqueue endpoints still run the quality gate, so disabling the general editorial-approval preference does not bypass these new requirements. No real uploads or provider generation are performed during testing.

## Operator walkthrough

1. Create/open a production using the existing topic and niche flow. Register its sources in **Investigación**.
2. Open **Dirección editorial**. Complete **Tema y enfoque**, including a specific question, premise, contribution and audience.
3. Review editorial defaults, choose/customize a narrative and optionally request the explicitly heuristic recommendation. Specify visual direction and local quality targets.
4. In **Configuración NotebookLM**, choose compatible format, language and style. Keep entitlement status unverified unless actually observed; include evidence for restricted features.
5. In **Fuentes y paquete**, select registered sources, save, review similarity findings, acknowledge legitimate continuity and inspect/edit the four composed blocks. Copy blocks or export Markdown. Approve the current preview to freeze it.
6. Follow the existing **NotebookLM** tasks in the official interface. If applicable, record actual producer-note review. Register actual settings and source correspondence in **Generación y revisión**.
7. Import the genuine original through **Vídeo y calidad**, wait for inspection, complete tasks, select the canonical file and prepare its thumbnail. Record evidence for all detailed editorial criteria, then complete the existing final technical/editorial review.
8. Use **Publicación** for reviewed metadata and separately authorized uploads. This extension grants no upload, public-visibility or paid-resource authorization.

## Reproducible verification

Use the installed .NET 10 SDK; this machine's system `dotnet` resolves to .NET 9, so commands used `C:\Users\mozot\AppData\Local\NotebookYt\dotnet\dotnet.exe` or prepended that directory to PATH. Tool versions: .NET 10.0.403, Node 24.22.1. PostgreSQL test instance: loopback port 55435, isolated from the operator database on 55434. FFmpeg/FFprobe use their locally installed WinGet paths.

Commands executed:

```powershell
git status --short
git branch --show-current
git log -1 --oneline
git switch -c feat/editorial-intelligence
dotnet tool restore
dotnet build src/NotebookYt.Api --nologo -v:q
dotnet ef migrations add EditorialIntelligence --project src/NotebookYt.Api --no-build
dotnet test tests/NotebookYt.Tests --nologo -v:q --logger 'trx;LogFileName=editorial-intelligence.trx'
npm --prefix web run typecheck
npm --prefix web test -- --reporter=dot
npm --prefix web run build
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Start-LocalAcceptance.ps1 -PgBin <local-pg-bin> -FfmpegBin <local-ffmpeg-bin> -Dotnet <dotnet-10> -PostgresPort 55435 -HttpPort 5082
npm --prefix web run test:e2e -- --reporter=line
git diff --check
```

Backend tests require `NOTEBOOK_TEST_POSTGRES` pointing to a dedicated local administrator database, `TEST_FFMPEG`, `TEST_FFPROBE` and `TEST_PG_BIN`. The fixture creates and drops only random test databases. E2E uses `PLAYWRIGHT_BASE_URL=http://127.0.0.1:5082` and `TEST_FFMPEG`; authentication remains enabled with the documented isolated test-only password. Never run this suite against operator data.

Final results and deployment evidence are recorded in `EDITORIAL_INTELLIGENCE_DELIVERY.md`.

Earlier failures were corrected, not counted as passing: an unsupported `dotnet ef --no-connect` argument; two backend expectations affected by the new contract (actual-settings acknowledgement and migration count); frontend test selectors/encoding and malformed test fixtures; an E2E run missing `TEST_FFMPEG`; populated textarea label matching. Explicit control IDs and label associations were added. The first controlled restart refused an occupied acceptance port; ownership was then verified against the recorded test PID before stopping it. No unknown process or operator database was affected.

## External limitations

The new capabilities are locally implemented and tested. The official documentation was checked live; current account entitlements, the newly configured packages' actual Google behavior and a newly generated genuine export have not been verified. NotebookLM generation and official download remain manual. Recommendations do not require a paid provider and do not manufacture research evidence. VideoNews AI Studio is outside this change.
