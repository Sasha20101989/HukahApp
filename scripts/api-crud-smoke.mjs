const apiUrl = trimTrailingSlash(process.env.API_URL ?? "http://localhost:8080");
const ownerPhone = process.env.SMOKE_OWNER_PHONE ?? "+79990000000";
const ownerPassword = process.env.SMOKE_OWNER_PASSWORD ?? "password";
const timeoutMs = Number(process.env.SMOKE_TIMEOUT_MS ?? 12000);
const runId = new Date().toISOString().replace(/\D/g, "").slice(0, 14);
const prefix = `SMOKE-${runId}`;

const cleanup = [];
const results = [];
let accessToken = "";
let clientId = process.env.SMOKE_CLIENT_ID ?? "";

try {
  await step("auth login owner", async () => {
    const auth = await api("POST", "/api/auth/login", { phone: ownerPhone, password: ownerPassword }, { auth: false });
    accessToken = auth.accessToken;
    assert(accessToken, "access token was not returned");
  });

  if (!clientId) {
    clientId = await step("auth register smoke client", async () => {
      const phone = `+7222${runId.slice(-7)}`;
      const registered = await api("POST", "/api/auth/register", {
        name: `${prefix} Client`,
        phone,
        email: `client-${runId}@hookah.local`,
        password: "password"
      }, { auth: false });
      assert(registered.userId, "registered client id was not returned");
      cleanup.unshift(() => api("DELETE", `/api/users/${registered.userId}`));
      return registered.userId;
    });
  }

  const branch = await step("branch create/update", async () => {
    const created = await api("POST", "/api/branches", {
      name: `${prefix} Branch`,
      address: "CRUD smoke address",
      phone: `+7000${runId.slice(-8)}`,
      timezone: "Europe/Moscow"
    });
    cleanup.unshift(() => api("PATCH", `/api/branches/${created.id}`, { isActive: false }));
    const updated = await api("PATCH", `/api/branches/${created.id}`, { name: `${prefix} Branch Updated` });
    assertEquals(updated.name, `${prefix} Branch Updated`, "branch name was not updated");
    return updated;
  });

  await step("branch working hours put/get", async () => {
    const rows = Array.from({ length: 7 }, (_, dayOfWeek) => ({ dayOfWeek, opensAt: "12:00:00", closesAt: "23:59:00", isClosed: false }));
    const saved = await api("PUT", `/api/branches/${branch.id}/working-hours`, rows);
    assert(Array.isArray(saved) && saved.length === 7, "working hours were not saved");
  });

  const hall = await step("hall create/update", async () => {
    const created = await api("POST", "/api/halls", { branchId: branch.id, name: `${prefix} Hall`, description: "smoke hall" });
    cleanup.unshift(() => api("DELETE", `/api/halls/${created.id}`));
    const updated = await api("PATCH", `/api/halls/${created.id}`, { name: `${prefix} Hall Updated`, description: "updated" });
    assertEquals(updated.name, `${prefix} Hall Updated`, "hall name was not updated");
    return updated;
  });

  const zone = await step("zone create/update/delete", async () => {
    const created = await api("POST", "/api/zones", { branchId: branch.id, name: `${prefix} Zone`, description: "smoke zone", color: "#1e765f", xPosition: 20, yPosition: 30, width: 240, height: 180 });
    cleanup.unshift(() => api("DELETE", `/api/zones/${created.id}`));
    const updated = await api("PATCH", `/api/zones/${created.id}`, { name: `${prefix} Zone Updated`, xPosition: 44, yPosition: 55 });
    assertEquals(Number(updated.xPosition), 44, "zone x position was not updated");
    return updated;
  });

  const table = await step("table create/update/status/delete", async () => {
    const created = await api("POST", "/api/tables", { hallId: hall.id, zoneId: zone.id, name: `${prefix} Table`, capacity: 4, xPosition: 120, yPosition: 220 });
    cleanup.unshift(() => api("DELETE", `/api/tables/${created.id}`));
    const updated = await api("PATCH", `/api/tables/${created.id}`, { name: `${prefix} Table Updated`, capacity: 6, xPosition: 150, yPosition: 240 });
    assertEquals(updated.capacity, 6, "table capacity was not updated");
    const status = await api("PATCH", `/api/tables/${created.id}/status`, { status: "CLEANING" });
    assertEquals(status.status, "CLEANING", "table status was not updated");
    return updated;
  });

  await step("floor plan reflects resources", async () => {
    const floor = await api("GET", `/api/branches/${branch.id}/floor-plan`);
    assert(floor.halls.some((item) => item.id === hall.id), "hall missing from floor plan");
    assert(floor.zones.some((item) => item.id === zone.id), "zone missing from floor plan");
    assert(floor.tables.some((item) => item.id === table.id), "table missing from floor plan");
  });

  const hookah = await step("hookah create/update/status/delete", async () => {
    const created = await api("POST", "/api/hookahs", { branchId: branch.id, name: `${prefix} Hookah`, brand: "SmokeBrand", model: "CRUD", status: "AVAILABLE", photoUrl: null });
    cleanup.unshift(() => api("DELETE", `/api/hookahs/${created.id}`));
    const updated = await api("PATCH", `/api/hookahs/${created.id}`, { name: `${prefix} Hookah Updated`, brand: "SmokeBrand2", model: "CRUD2" });
    assertEquals(updated.brand, "SmokeBrand2", "hookah brand was not updated");
    const status = await api("PATCH", `/api/hookahs/${created.id}/status`, { status: "WASHING" });
    assertEquals(status.status, "WASHING", "hookah status was not updated");
    return updated;
  });

  const bowl = await step("bowl create/update/delete", async () => {
    const created = await api("POST", "/api/bowls", { name: `${prefix} Bowl`, type: "PHUNNEL", capacityGrams: 18, recommendedStrength: "MEDIUM", averageSmokeMinutes: 70 });
    cleanup.unshift(() => api("DELETE", `/api/bowls/${created.id}`));
    const updated = await api("PATCH", `/api/bowls/${created.id}`, { name: `${prefix} Bowl Updated`, capacityGrams: 20 });
    assertEquals(Number(updated.capacityGrams), 20, "bowl capacity was not updated");
    return updated;
  });

  const tobaccos = await step("tobacco create/update/delete", async () => {
    const first = await api("POST", "/api/tobaccos", { brand: `${prefix} Brand`, line: "Base", flavor: "Apple", strength: "MEDIUM", category: "FRUIT", description: "smoke", costPerGram: 8, photoUrl: null });
    const second = await api("POST", "/api/tobaccos", { brand: `${prefix} Brand`, line: "Base", flavor: "Mint", strength: "LIGHT", category: "FRESH", description: "smoke", costPerGram: 6, photoUrl: null });
    const third = await api("POST", "/api/tobaccos", { brand: `${prefix} Brand`, line: "Base", flavor: "Grape", strength: "MEDIUM", category: "FRUIT", description: "smoke", costPerGram: 7, photoUrl: null });
    cleanup.unshift(() => api("DELETE", `/api/tobaccos/${third.id}`));
    cleanup.unshift(() => api("DELETE", `/api/tobaccos/${second.id}`));
    cleanup.unshift(() => api("DELETE", `/api/tobaccos/${first.id}`));
    const updated = await api("PATCH", `/api/tobaccos/${first.id}`, { flavor: "Green Apple", costPerGram: 9 });
    assertEquals(updated.flavor, "Green Apple", "tobacco flavor was not updated");
    return [updated, second, third];
  });

  const mix = await step("mix create/calculate/update/visibility/delete", async () => {
    const calculated = await api("POST", "/api/mixes/calculate", { bowlId: bowl.id, items: [{ tobaccoId: tobaccos[0].id, percent: 40 }, { tobaccoId: tobaccos[1].id, percent: 35 }, { tobaccoId: tobaccos[2].id, percent: 25 }] });
    assertEquals(Number(calculated.totalGrams), 20, "mix grams were not calculated from bowl");
    const created = await api("POST", "/api/mixes", {
      name: `${prefix} Mix`,
      description: "crud smoke mix",
      bowlId: bowl.id,
      strength: "MEDIUM",
      tasteProfile: "FRUIT_FRESH",
      isPublic: false,
      createdBy: null,
      price: 990,
      items: [{ tobaccoId: tobaccos[0].id, percent: 50 }, { tobaccoId: tobaccos[1].id, percent: 30 }, { tobaccoId: tobaccos[2].id, percent: 20 }]
    });
    cleanup.unshift(() => api("DELETE", `/api/mixes/${created.id}`));
    const updated = await api("PATCH", `/api/mixes/${created.id}`, { name: `${prefix} Mix Updated`, price: 1090, isPublic: true, items: [{ tobaccoId: tobaccos[0].id, percent: 34 }, { tobaccoId: tobaccos[1].id, percent: 33 }, { tobaccoId: tobaccos[2].id, percent: 33 }] });
    assertEquals(updated.name, `${prefix} Mix Updated`, "mix name was not updated");
    assertEquals(updated.items.length, 3, "mix update must preserve arbitrary item count");
    const visibility = await api("PATCH", `/api/mixes/${created.id}/visibility`, { isPublic: false });
    assertEquals(visibility.isPublic, false, "mix visibility was not updated");
    return updated;
  });

  await step("inventory in/check/out/adjustment/movements", async () => {
    const receipt = await api("POST", "/api/inventory/in", { branchId: branch.id, tobaccoId: tobaccos[0].id, amountGrams: 120, costPerGram: 9, supplier: "CRUD smoke", comment: "CRUD smoke receipt" });
    const check = await api("POST", "/api/inventory/check", { branchId: branch.id, items: [{ tobaccoId: tobaccos[0].id, requiredGrams: 20 }] });
    assertEquals(check.isAvailable, true, "inventory availability failed after stock in");
    const minStock = await api("PATCH", `/api/inventory/${receipt.id}`, { minStockGrams: 75 });
    assertEquals(Number(minStock.minStockGrams), 75, "inventory min stock was not updated");
    await api("POST", "/api/inventory/out", { branchId: branch.id, tobaccoId: tobaccos[0].id, amountGrams: 10, reason: "CRUD smoke write-off" });
    await api("POST", "/api/inventory/adjustment", { branchId: branch.id, tobaccoId: tobaccos[0].id, newStockGrams: 50 });
    const movements = await api("GET", `/api/inventory/movements?branchId=${branch.id}&tobaccoId=${tobaccos[0].id}`);
    assert(movements.length >= 3, "inventory movements were not recorded");
  });

  const staff = await step("staff create/update/delete", async () => {
    const phone = `+7111${runId.slice(-7)}`;
    const created = await api("POST", "/api/users/staff", { name: `${prefix} Staff`, phone, role: "WAITER", branchId: branch.id });
    cleanup.unshift(() => api("DELETE", `/api/users/${created.id}`));
    const updated = await api("PATCH", `/api/users/${created.id}`, { name: `${prefix} Staff Updated`, email: `staff-${runId}@hookah.local`, branchId: branch.id, status: "active" });
    assertEquals(updated.name, `${prefix} Staff Updated`, "staff name was not updated");
    return updated;
  });

  await step("staff shift create/start/finish/cancel", async () => {
    const start = new Date(Date.now() + 24 * 60 * 60 * 1000);
    start.setUTCHours(9, 0, 0, 0);
    const finish = new Date(start.getTime() + 4 * 60 * 60 * 1000);
    const shift = await api("POST", "/api/staff/shifts", { staffId: staff.id, branchId: branch.id, startsAt: start.toISOString(), endsAt: finish.toISOString(), roleOnShift: "WAITER" });
    await api("PATCH", `/api/staff/shifts/${shift.id}/start`);
    const completed = await api("PATCH", `/api/staff/shifts/${shift.id}/finish`);
    assertEquals(completed.status, "COMPLETED", "shift was not completed");

    const cancelStart = new Date(start.getTime() + 24 * 60 * 60 * 1000);
    const cancelEnd = new Date(cancelStart.getTime() + 4 * 60 * 60 * 1000);
    const cancelShift = await api("POST", "/api/staff/shifts", { staffId: staff.id, branchId: branch.id, startsAt: cancelStart.toISOString(), endsAt: cancelEnd.toISOString(), roleOnShift: "WAITER" });
    const cancelled = await api("PATCH", `/api/staff/shifts/${cancelShift.id}/cancel`, { reason: "CRUD smoke cancel" });
    assertEquals(cancelled.status, "CANCELLED", "shift was not cancelled");
  });

  await step("notifications template/send/read/delete", async () => {
    const code = `${prefix}.manual`.toLowerCase();
    const template = await api("POST", "/api/notifications/templates", { code, channel: "CRM", title: `${prefix} Title`, message: `${prefix} Message` });
    cleanup.unshift(() => api("DELETE", `/api/notifications/templates/${encodeURIComponent(code)}`));
    const updated = await api("PUT", `/api/notifications/templates/${encodeURIComponent(code)}`, { code, channel: "CRM", title: `${prefix} Title Updated`, message: `${prefix} Message Updated` });
    assertEquals(updated.title, `${prefix} Title Updated`, "notification template was not updated");
    const notification = await api("POST", "/api/notifications/send", { userId: clientId, channel: "CRM", title: `${prefix} Notice`, message: "CRUD smoke notification" });
    cleanup.unshift(() => api("DELETE", `/api/notifications/${notification.id}`));
    const read = await api("PATCH", `/api/notifications/${notification.id}/read`, {});
    assert(read.readAt || read.isRead !== false, "notification was not marked read");
  });

  await step("review create/update/delete", async () => {
    const review = await api("POST", "/api/reviews", { clientId, mixId: mix.id, orderId: null, rating: 5, text: `${prefix} review` });
    cleanup.unshift(() => api("DELETE", `/api/reviews/${review.id}`));
    const updated = await api("PATCH", `/api/reviews/${review.id}`, { rating: 4, text: `${prefix} review updated` });
    assertEquals(updated.rating, 4, "review rating was not updated");
    const summary = await api("GET", `/api/reviews/mixes/${mix.id}/summary`);
    assert(summary.reviewsCount >= 1, "review summary did not include created review");
  });

  await step("promocode create/validate/deactivate", async () => {
    const code = `SMOKE${runId.slice(-8)}`;
    const today = new Date().toISOString().slice(0, 10);
    const validTo = new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString().slice(0, 10);
    const promo = await api("POST", "/api/promocodes", { code, discountType: "PERCENT", discountValue: 10, validFrom: today, validTo, maxRedemptions: 10, perClientLimit: 1 });
    cleanup.unshift(() => api("PATCH", `/api/promocodes/${promo.code}/deactivate`));
    const updated = await api("PATCH", `/api/promocodes/${promo.code}`, { discountType: "PERCENT", discountValue: 12, validFrom: today, validTo, maxRedemptions: 10, perClientLimit: 1, isActive: true });
    assertEquals(Number(updated.discountValue), 12, "promocode discount was not updated");
    const validation = await api("POST", "/api/promocodes/validate", { code, clientId, orderAmount: 1000 });
    assertEquals(validation.isValid, true, "promocode was not valid");
    const deactivated = await api("PATCH", `/api/promocodes/${code}/deactivate`);
    assertEquals(deactivated.isActive, false, "promocode was not deactivated");
  });

  await cleanupResources();
} catch (error) {
  await cleanupResources();
  printReport(results);
  console.error(error instanceof Error ? error.message : String(error));
  process.exit(1);
}

printReport(results);

async function step(name, action) {
  const startedAt = performance.now();
  try {
    const value = await action();
    results.push({ name, ok: true, durationMs: Math.round(performance.now() - startedAt) });
    return value;
  } catch (error) {
    results.push({ name, ok: false, durationMs: Math.round(performance.now() - startedAt), error: error instanceof Error ? error.message : String(error) });
    throw new Error(`${name}: ${error instanceof Error ? error.message : String(error)}`);
  }
}

async function api(method, path, payload, options = {}) {
  const auth = options.auth !== false;
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const response = await fetch(`${apiUrl}${path}`, {
      method,
      headers: {
        "Content-Type": "application/json",
        ...(auth && accessToken ? { Authorization: `Bearer ${accessToken}` } : {})
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

function assertEquals(actual, expected, message) {
  if (actual !== expected) throw new Error(`${message}: expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`);
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

function printReport(items) {
  for (const item of items) {
    if (item.ok) {
      console.log(`ok ${item.name} ${item.durationMs}ms`);
    } else {
      console.error(`fail ${item.name} ${item.durationMs}ms: ${item.error}`);
    }
  }
}

function trimTrailingSlash(value) {
  return value.replace(/\/+$/, "");
}
