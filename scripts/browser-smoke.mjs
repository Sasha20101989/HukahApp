import { existsSync, mkdtempSync, rmSync } from "node:fs";
import { createServer } from "node:net";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { spawn } from "node:child_process";

const crmUrl = trimTrailingSlash(process.env.CRM_URL ?? "http://localhost:3000");
const clientUrl = trimTrailingSlash(process.env.CLIENT_URL ?? "http://localhost:3001");
const timeoutMs = Number(process.env.SMOKE_TIMEOUT_MS ?? 15000);
const chrome = findChrome();

const scenarios = [
  { name: "crm login renders", url: `${crmUrl}/login`, markers: ["Hookah CRM", "Вход персонала", "Телефон", "Пароль"] },
  { name: "crm middleware redirects to login", url: crmUrl, markers: ["Hookah CRM", "Вход персонала"] },
  { name: "client booking renders", url: clientUrl, markers: ["Hookah Place", "PWA BOOKING", "Итог брони"] },
  { name: "client account guard redirects home", url: `${clientUrl}/account`, markers: ["Hookah Place", "PWA BOOKING"] },
  { name: "payment return success renders", url: `${clientUrl}/payment/return?status=success&paymentId=smoke`, markers: ["Статус оплаты", "SUCCESS"] },
  { name: "payment return failed renders", url: `${clientUrl}/payment/return?status=failed&paymentId=smoke`, markers: ["Статус оплаты", "FAILED"] },
  { name: "payment return processing renders", url: `${clientUrl}/payment/return?status=processing&paymentId=smoke&bookingId=booking-smoke`, markers: ["Статус оплаты", "PROCESSING", "booking-smoke"] },
  { name: "payment return missing id renders", url: `${clientUrl}/payment/return?status=processing`, markers: ["Статус оплаты", "Платеж не передан"] }
];

if (!globalThis.WebSocket) {
  throw new Error("Node.js WebSocket global is required for browser smoke. Use Node 22+ or run the workspace pnpm through the configured runtime.");
}

async function renderText(port, url) {
  const target = await createTarget(port, url);
  const cdp = new CdpClient(target.webSocketDebuggerUrl);
  try {
    await cdp.open();
    await cdp.send("Runtime.enable");
    await cdp.send("Page.enable");
    await waitUntil(async () => {
      const result = await cdp.send("Runtime.evaluate", { expression: "document.body?.innerText || ''", returnByValue: true });
      return String(result.result?.value ?? "");
    }, (text) => Boolean(text.trim()) && !text.includes("Загружаем сессию..."), timeoutMs);
    const finalResult = await cdp.send("Runtime.evaluate", { expression: "document.body?.innerText || ''", returnByValue: true });
    return String(finalResult.result?.value ?? "");
  } finally {
    cdp.close();
    await closeTarget(port, target.id).catch(() => undefined);
  }
}

async function main() {
  const userDataDir = mkdtempSync(join(tmpdir(), "hookah-browser-smoke-"));
  const debugPort = await getFreePort();
  const browser = spawn(chrome, [
    "--headless=new",
    "--disable-gpu",
    "--disable-dev-shm-usage",
    "--no-sandbox",
    "--no-first-run",
    "--no-default-browser-check",
    `--user-data-dir=${userDataDir}`,
    `--remote-debugging-port=${debugPort}`,
    "about:blank"
  ], { stdio: ["ignore", "ignore", "pipe"] });

  const stderr = [];
  browser.stderr.on("data", (chunk) => stderr.push(chunk.toString()));

  try {
    await waitForChrome(debugPort, stderr);
    const results = [];
    for (const scenario of scenarios) {
      const startedAt = performance.now();
      try {
        const text = await renderText(debugPort, scenario.url);
        assertText(scenario, text);
        results.push({ name: scenario.name, ok: true, durationMs: Math.round(performance.now() - startedAt) });
      } catch (error) {
        results.push({ name: scenario.name, ok: false, error: error instanceof Error ? error.message : String(error), durationMs: Math.round(performance.now() - startedAt) });
      }
    }

    for (const item of results) {
      if (item.ok) {
        console.log(`ok ${item.name} ${item.durationMs}ms`);
      } else {
        console.error(`fail ${item.name} ${item.durationMs}ms: ${item.error}`);
      }
    }

    if (results.some((result) => !result.ok)) process.exitCode = 1;
  } finally {
    browser.kill("SIGTERM");
    await waitForProcessExit(browser).catch(() => browser.kill("SIGKILL"));
    rmSync(userDataDir, { recursive: true, force: true, maxRetries: 3, retryDelay: 100 });
  }
}

async function createTarget(port, url) {
  const response = await fetch(`http://127.0.0.1:${port}/json/new?${encodeURIComponent(url)}`, { method: "PUT" });
  if (!response.ok) throw new Error(`cannot create Chrome target: HTTP ${response.status}`);
  return response.json();
}

async function closeTarget(port, id) {
  await fetch(`http://127.0.0.1:${port}/json/close/${id}`);
}

async function waitForChrome(port, stderr) {
  const deadline = Date.now() + timeoutMs;
  let lastError = "";
  while (Date.now() < deadline) {
    try {
      const response = await fetch(`http://127.0.0.1:${port}/json/version`);
      if (response.ok) return;
      lastError = `HTTP ${response.status}`;
    } catch (error) {
      lastError = error instanceof Error ? error.message : String(error);
    }
    await sleep(100);
  }
  throw new Error(`Chrome DevTools did not start: ${lastError || stderr.join("").slice(-500)}`);
}

async function waitUntil(read, done, timeout) {
  const deadline = Date.now() + timeout;
  let value;
  while (Date.now() < deadline) {
    value = await read();
    if (done(value)) return value;
    await sleep(150);
  }
  return value;
}

function assertText(scenario, text) {
  for (const marker of scenario.markers) {
    if (!text.includes(marker)) throw new Error(`missing marker "${marker}". Text: ${compact(text).slice(0, 500)}`);
  }
  const forbidden = ["Application error: a client-side exception", "NEXT_HTTP_ERROR_FALLBACK", "404: This page could not be found"];
  const match = forbidden.find((marker) => text.includes(marker));
  if (match) throw new Error(`runtime error marker found: ${match}`);
}

class CdpClient {
  constructor(url) {
    this.url = url;
    this.nextId = 1;
    this.pending = new Map();
  }

  open() {
    return new Promise((resolve, reject) => {
      this.socket = new WebSocket(this.url);
      this.socket.addEventListener("open", () => resolve());
      this.socket.addEventListener("error", () => reject(new Error("Chrome DevTools WebSocket failed")));
      this.socket.addEventListener("message", (event) => this.handleMessage(event.data));
    });
  }

  send(method, params = {}) {
    const id = this.nextId++;
    const payload = JSON.stringify({ id, method, params });
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.socket.send(payload);
    });
  }

  handleMessage(raw) {
    const message = JSON.parse(raw);
    if (!message.id || !this.pending.has(message.id)) return;
    const pending = this.pending.get(message.id);
    this.pending.delete(message.id);
    if (message.error) {
      pending.reject(new Error(message.error.message));
    } else {
      pending.resolve(message.result);
    }
  }

  close() {
    this.socket?.close();
  }
}

function getFreePort() {
  return new Promise((resolve, reject) => {
    const server = createServer();
    server.listen(0, "127.0.0.1", () => {
      const address = server.address();
      server.close(() => typeof address === "object" && address ? resolve(address.port) : reject(new Error("cannot allocate local port")));
    });
    server.on("error", reject);
  });
}

function findChrome() {
  const candidates = [
    process.env.CHROME_BIN,
    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
    "/Applications/Chromium.app/Contents/MacOS/Chromium",
    "/usr/bin/google-chrome",
    "/usr/bin/chromium",
    "/usr/bin/chromium-browser"
  ].filter(Boolean);

  const found = candidates.find((item) => existsSync(item));
  if (!found) throw new Error("Chrome/Chromium binary not found. Set CHROME_BIN to run browser smoke.");
  return found;
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function waitForProcessExit(process) {
  if (process.exitCode !== null) return Promise.resolve();
  return new Promise((resolve) => {
    const timer = setTimeout(resolve, 1000);
    process.once("exit", () => {
      clearTimeout(timer);
      resolve();
    });
  });
}

function compact(value) {
  return value.replace(/\s+/g, " ").trim();
}

function trimTrailingSlash(value) {
  return value.replace(/\/+$/, "");
}

await main();
