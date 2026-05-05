const apiUrl = trimTrailingSlash(process.env.API_URL ?? process.env.GATEWAY_URL ?? "http://localhost:8080");
const ownerPhone = process.env.SMOKE_OWNER_PHONE ?? "+79990000000";
const ownerPassword = process.env.SMOKE_OWNER_PASSWORD ?? "password";
const timeoutMs = Number(process.env.SMOKE_TIMEOUT_MS ?? 12000);
const runId = new Date().toISOString().replace(/\D/g, "").slice(0, 14);
const prefix = `TENANT-SMOKE-${runId}`;

const cleanup = [];
let accessToken = "";

try {
  accessToken = (await api("POST", "/api/auth/login", { phone: ownerPhone, password: ownerPassword }, { auth: false })).accessToken;
  assert(accessToken, "access token was not returned");

  const tenants = await api("GET", "/api/tenants");
  assert(Array.isArray(tenants), "tenants response must be an array");

  const demo = tenants.find((t) => (t.slug ?? "").toLowerCase() === "demo");
  assert(demo?.id, "demo tenant was not found (expected slug 'demo')");

  let other = tenants.find((t) => (t.slug ?? "").toLowerCase() === "other");
  if (!other) {
    other = await api("POST", "/api/tenants", { name: "Other Tenant", slug: "other" });
  }
  assert(other?.id, "other tenant was not created");

  const branch = await api("POST", "/api/branches", {
    name: `${prefix} Branch`,
    address: "Tenant isolation smoke",
    phone: `+7000${runId.slice(-8)}`,
    timezone: "Europe/Moscow"
  }, undefined, { tenantId: demo.id });
  assert(branch?.id, "branch id was not returned");
  cleanup.unshift(() => api("PATCH", `/api/branches/${branch.id}`, { isActive: false }, undefined, { tenantId: demo.id }));

  const demoBranches = await api("GET", "/api/branches", undefined, { tenantId: demo.id });
  assert(Array.isArray(demoBranches), "demo branches must be an array");
  assert(demoBranches.some((b) => b.id === branch.id), "branch was not visible in its tenant");

  const otherBranches = await api("GET", "/api/branches", undefined, { tenantId: other.id });
  assert(Array.isArray(otherBranches), "other tenant branches must be an array");
  if (otherBranches.some((b) => b.id === branch.id)) {
    throw new Error("cross-tenant branch leaked to other tenant");
  }

  await cleanupResources();
  console.log("tenant isolation smoke passed");
} catch (error) {
  await cleanupResources();
  console.error(error instanceof Error ? error.message : String(error));
  process.exit(1);
}

async function api(method, path, payload, queryOrOptions, maybeOptions) {
  const [query, options] = normalizeArgs(queryOrOptions, maybeOptions);
  const auth = options.auth !== false;
  const tenantId = options.tenantId;

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const response = await fetch(`${apiUrl}${path}${query ?? ""}`, {
      method,
      headers: {
        "Content-Type": "application/json",
        ...(auth && accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
        ...(tenantId ? { "X-Tenant-Id": tenantId } : {})
      },
      body: payload === undefined ? undefined : JSON.stringify(payload),
      signal: controller.signal
    });

    const text = await response.text();
    if (!response.ok) throw new Error(`${method} ${path} -> HTTP ${response.status}: ${readProblem(text)}`);
    if (response.status === 204 || !text) return undefined;
    return JSON.parse(text);
  } finally {
    clearTimeout(timer);
  }
}

function normalizeArgs(queryOrOptions, maybeOptions) {
  if (queryOrOptions && typeof queryOrOptions === "string") return [queryOrOptions, maybeOptions ?? {}];
  return [undefined, queryOrOptions ?? {}];
}

async function cleanupResources() {
  const failures = [];
  for (const clean of cleanup.splice(0)) {
    try {
      await clean();
    } catch (error) {
      failures.push(error instanceof Error ? error.message : String(error));
    }
  }
  if (failures.length > 0) {
    console.error(`cleanup warnings: ${failures.join(" | ")}`);
  }
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function readProblem(text) {
  if (!text) return "";
  try {
    const parsed = JSON.parse(text);
    return parsed.message ?? parsed.Message ?? parsed.title ?? parsed.code ?? text;
  } catch {
    return text;
  }
}

function trimTrailingSlash(value) {
  return value.replace(/\/+$/, "");
}
