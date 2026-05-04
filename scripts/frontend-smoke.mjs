const crmUrl = trimTrailingSlash(process.env.CRM_URL ?? "http://localhost:3000");
const clientUrl = trimTrailingSlash(process.env.CLIENT_URL ?? "http://localhost:3001");
const timeoutMs = Number(process.env.SMOKE_TIMEOUT_MS ?? 8000);

const scenarios = [
  {
    name: "crm login page",
    url: `${crmUrl}/login`,
    markers: ["Hookah CRM", "Вход персонала"]
  },
  {
    name: "crm route guard",
    url: crmUrl,
    markers: ["Hookah CRM", "Вход персонала"],
    finalUrlIncludes: "/login"
  },
  {
    name: "client booking home",
    url: clientUrl,
    markers: ["Hookah Booking", "Загружаем сессию"]
  },
  {
    name: "client account guard",
    url: `${clientUrl}/account`,
    markers: ["Hookah Booking"],
    finalUrlIncludes: "login=required"
  },
  {
    name: "payment return success",
    url: `${clientUrl}/payment/return?status=success&paymentId=smoke`,
    markers: ["Статус оплаты"]
  },
  {
    name: "payment return failed",
    url: `${clientUrl}/payment/return?status=failed&paymentId=smoke`,
    markers: ["Статус оплаты"]
  },
  {
    name: "payment return processing",
    url: `${clientUrl}/payment/return?status=processing&paymentId=smoke&bookingId=booking-smoke`,
    markers: ["Статус оплаты", "PROCESSING", "booking-smoke"]
  },
  {
    name: "payment return missing id",
    url: `${clientUrl}/payment/return?status=processing`,
    markers: ["Статус оплаты", "Платеж не передан"]
  },
  {
    name: "crm manifest",
    url: `${crmUrl}/manifest.webmanifest`,
    markers: ["Hookah CRM"]
  },
  {
    name: "client manifest",
    url: `${clientUrl}/manifest.webmanifest`,
    markers: ["Hookah Place"]
  }
];

const results = [];
for (const scenario of scenarios) {
  const startedAt = performance.now();
  try {
    const response = await fetchWithTimeout(scenario.url, timeoutMs);
    const body = await response.text();
    assertResponse(scenario, response, body);
    results.push({ name: scenario.name, ok: true, status: response.status, finalUrl: response.url, durationMs: Math.round(performance.now() - startedAt) });
  } catch (error) {
    results.push({ name: scenario.name, ok: false, error: error instanceof Error ? error.message : String(error), durationMs: Math.round(performance.now() - startedAt) });
  }
}

printReport(results);
if (results.some((result) => !result.ok)) process.exit(1);

async function fetchWithTimeout(url, timeout) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeout);
  try {
    return await fetch(url, { redirect: "follow", signal: controller.signal });
  } finally {
    clearTimeout(timer);
  }
}

function assertResponse(scenario, response, body) {
  if (!response.ok) {
    throw new Error(`HTTP ${response.status} for ${response.url}`);
  }
  if (scenario.finalUrlIncludes && !response.url.includes(scenario.finalUrlIncludes)) {
    throw new Error(`expected final URL to include "${scenario.finalUrlIncludes}", got "${response.url}"`);
  }
  for (const marker of scenario.markers) {
    if (!body.includes(marker)) {
      throw new Error(`missing marker "${marker}"`);
    }
  }
  assertNoRuntimeError(body);
}

function assertNoRuntimeError(body) {
  const forbidden = ["Application error: a client-side exception", "NEXT_HTTP_ERROR_FALLBACK", "<h1>404</h1>"];
  const match = forbidden.find((marker) => body.includes(marker));
  if (match) throw new Error(`runtime error marker found: ${match}`);
}

function printReport(items) {
  for (const item of items) {
    if (item.ok) {
      console.log(`ok ${item.name} ${item.status} ${item.durationMs}ms`);
    } else {
      console.error(`fail ${item.name} ${item.durationMs}ms: ${item.error}`);
    }
  }
}

function trimTrailingSlash(value) {
  return value.replace(/\/+$/, "");
}
