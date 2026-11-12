import os from 'node:os';
import path from 'node:path';
import { defineConfig, devices } from '@playwright/test';

// Execute pelo script `scripts/e2e.ps1`: ele cria o banco de E2E, gera a senha de demonstração e exporta
// E2E_PASSWORD / E2E_CONNECTION. Portas próprias evitam conflito com o ambiente de desenvolvimento.
const API_PORT = 5090;
const WEB_PORT = 5183;

for (const name of ['E2E_PASSWORD', 'E2E_CONNECTION']) {
  if (!process.env[name]) throw new Error(`${name} não definido. Rode: .\\scripts\\e2e.ps1`);
}

export default defineConfig({
  testDir: './e2e',
  // Os cenários compartilham o mesmo banco e usam sessões em tempo real: execução sequencial e determinística.
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 90_000,
  expect: { timeout: 12_000 },
  reporter: [['list'], ['html', { open: 'never', outputFolder: 'playwright-report' }]],
  use: {
    baseURL: `http://localhost:${WEB_PORT}`,
    locale: 'pt-BR',
    timezoneId: 'America/Sao_Paulo',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    {
      command: `dotnet run --project ../backend/src/HelpDeskPro.Api --no-build --no-launch-profile --urls http://localhost:${API_PORT}`,
      url: `http://localhost:${API_PORT}/openapi/v1.json`,
      reuseExistingServer: false,
      timeout: 120_000,
      env: {
        ASPNETCORE_ENVIRONMENT: 'Development',
        ConnectionStrings__Default: process.env.E2E_CONNECTION!,
        Seed__InitialPassword: process.env.E2E_PASSWORD!,
        Sla__CheckIntervalSeconds: '0',
        RateLimiting__AuthPermitPerMinute: '2000',
        Attachments__StoragePath: path.join(os.tmpdir(), 'hdp-e2e-attachments'),
      },
    },
    {
      command: 'npm run dev',
      url: `http://localhost:${WEB_PORT}`,
      reuseExistingServer: false,
      timeout: 60_000,
      env: { VITE_PORT: String(WEB_PORT), API_URL: `http://localhost:${API_PORT}` },
    },
  ],
});
