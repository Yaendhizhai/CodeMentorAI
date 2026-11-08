# CodeMentor AI UI

Responsive Angular 22 standalone application for registering and inspecting CodeMentor AI repository projects.

## Prerequisites

- Node.js compatible with npm 11
- npm 11
- .NET 10 SDK for running the API

## Install

```bash
npm install
```

## Run locally

Start the API in one terminal:

```powershell
dotnet run --project ..\..\CodeMentor.API\CodeMentor.API.csproj --launch-profile http
```

The API listens at `http://localhost:5269`. Configure its SQL Server connection string through the usual .NET configuration or secret provider.

Start Angular from `frontend/CodeMentor.UI` in another terminal:

```bash
npm start
```

Open `http://localhost:4200`. The Angular development proxy forwards `/api` requests to `http://localhost:5269`, so local development does not require broad CORS configuration.

## Build and tests

```bash
npm run build
npm test -- --watch=false
```

## API configuration

`src/environments/environment.ts` defines the frontend API base as `/api`. For local development, `proxy.conf.json` forwards that path to the API's HTTP launch profile at `http://localhost:5269`. In another deployment, route `/api` through the same-origin reverse proxy or change the environment configuration for that build. Do not put credentials or production secrets in frontend configuration.

The backend currently exposes:

- `POST /api/repository-projects` — register a public GitHub repository.
- `GET /api/repository-projects` — list registered repository projects.
- `GET /api/repository-projects/{id}` — retrieve one repository project.
- `POST /api/repository-projects/{id}/ingest` — synchronously ingest a registered repository. Use the **Ingest** action on its repository-list row; the UI displays the returned file/chunk counts and refreshes the list. Existing ingested repositories return a conflict, and repositories beyond ingestion limits show a size-limit message.
- `POST /api/repository-projects/{id}/code-search` — lexically search code chunks within the selected project. In Code Explorer, select a repository, enter a query, and choose a result limit (10, 25, 50, or 100). Results include repository-relative paths, chunk indices, line ranges, content, and match scores. Search is not semantic and does not call an AI service.

Analysis-session history and AI conversation endpoints are currently unavailable. Corresponding pages explain the missing API capability and do not fabricate results or citations.

## Structure

```text
src/app/
  core/
    models/       Typed API models
    services/     Repository API and centralized errors
  shared/
    components/   Page heading and unavailable-feature notice
  features/
    dashboard/
    repositories/
    repository-details/
    code-explorer/
    ai-mentor/
    analysis-history/
    settings/
  app.config.ts
  app.routes.ts   Lazy-loaded standalone feature routes
```
