# Notebook-yt — Phase 1 (in progress)

Local editorial foundation for one operator and one YouTube channel. **Not production ready.** The repository is public; do not expose the API on a public network without authentication.

## Requirements

- .NET 10 SDK
- PostgreSQL (local)
- Node.js 22+

## Local startup

1. Create an empty PostgreSQL database and a local database user with limited privileges.
2. Set `ConnectionStrings__Editorial` in your local shell to a PostgreSQL connection string. Do not commit credentials.
3. Run `dotnet restore src/NotebookYt.Api/NotebookYt.Api.csproj`.
4. **Database migrations have not yet been generated. The backend cannot be started against a fresh database until a migration is created and applied.** With `dotnet-ef` installed: `dotnet ef migrations add InitialEditorial --project src/NotebookYt.Api`, then `dotnet ef database update --project src/NotebookYt.Api`.
5. Run `dotnet run --project src/NotebookYt.Api --urls http://127.0.0.1:5080`.
6. In `web/`, run `npm install` and `npm run dev`.

## Scope and limitations

The backend includes local channel configuration, seven neutral weekday slots, niche editing, production creation, immutable niche name snapshots, status transitions with audit history, and external NotebookLM URL registration. No NotebookLM operations are automated. The frontend is an initial operator interface.

**Outstanding:** committed EF migrations, compile and runtime verification, backend and frontend tests, persistence restart test, stronger concurrency controls, and security review. Do not consider Phase 1 accepted until these are complete.

## NotebookLM feasibility

Official Google Cloud documentation describes preview APIs for Gemini Notebook Enterprise notebook creation, retrieval, listing, deletion, sharing, and source management. These require an eligible Enterprise setup and licensing. Personal-account API availability is not established. Video overview generation/download API support is not established. Keep these operations manual. See Google's Notebook Enterprise API documentation.
