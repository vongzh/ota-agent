# Frontend browser e2e (Playwright)

Minimal Chromium smoke for StayOTA Agent UI.

## Default (CI / self-contained)

No Postgres, Redis, or Host required. Playwright fulfills `/health` and `/api/*`
via route mocks while `vite preview` serves the built SPA.

```bash
cd frontend
npm ci
npm run build
npx playwright install chromium
npm run test:e2e
```

Covers:

1. App shell loads (brand + nav)
2. Workspace scenario list + mocked SSE agent happy path
3. Console plugins + Sessions panel

## Live against docker-compose (optional)

When you want a real Deterministic Host instead of mocks:

```bash
# terminal 1 — infra + optional app profile
docker compose up -d postgres redis
export PATH="$HOME/.dotnet:$PATH"
export STAYOTA_AGENT_ROOT="$(pwd)"
dotnet run --project backend/src/Hosts/StayOta.Agent.Host --urls http://127.0.0.1:5088

# terminal 2 — Vite with API proxy (vite.config.ts → :5088)
cd frontend && npm run dev

# terminal 3
cd frontend
E2E_LIVE=1 E2E_BASE_URL=http://127.0.0.1:5173 npm run test:e2e
```

Or full container stack:

```bash
docker compose --profile app up --build -d
# serve frontend separately (or proxy) then:
E2E_LIVE=1 E2E_BASE_URL=http://127.0.0.1:5173 npm run test:e2e
```

Live mode skips route mocks; assertions that depend on fixed mock copy
(`E2E 演示酒店`, `¥688`) may need looser matchers or scenario seeds.
CI always uses the mocked path.
