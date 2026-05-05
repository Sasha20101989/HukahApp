const API_BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080";

export type AuthResponse = { userId: string; accessToken: string; refreshToken: string };
export type RoleCode = "OWNER" | "MANAGER" | "HOOKAH_MASTER" | "WAITER" | "CLIENT";
export type PermissionCode = "tenants.manage" | "branches.manage" | "staff.manage" | "mixes.manage" | "inventory.manage" | "orders.manage" | "bookings.manage" | "analytics.read" | "bookings.create" | "*";
export type PermissionDefinition = { code: string; description: string };
export type TenantRole = { id: string; name: string; code: string; isSystem: boolean; isActive: boolean; permissions: string[] };
export type UserProfile = { id: string; name: string; phone: string; email?: string | null; role: RoleCode; branchId?: string | null; status: string };
export type Tenant = { id: string; slug: string; name: string; isActive: boolean; createdAt: string };
export type TenantSettings = { tenantId: string; defaultTimezone: string; defaultCurrency: string; requireDeposit: boolean };
export type AuditLog = { id: string; tenantId?: string | null; actorUserId?: string | null; action: string; targetType: string; targetId?: string | null; result: string; correlationId?: string | null; metadataJson?: string | null; createdAt: string };
export type Branch = { id: string; name: string; address: string; phone: string; timezone: string; isActive: boolean };
export type BranchWorkingHours = { branchId: string; dayOfWeek: number; opensAt: string; closesAt: string; isClosed: boolean };
export type FloorPlan = { branchId: string; halls: Hall[]; zones: Zone[]; tables: Table[] };
export type Hall = { id: string; branchId: string; name: string; description?: string | null };
export type Zone = { id: string; branchId: string; name: string; description?: string | null; color?: string | null; xPosition: number; yPosition: number; width: number; height: number; isActive: boolean };
export type Table = { id: string; hallId: string; zoneId?: string | null; name: string; capacity: number; status: string; xPosition: number; yPosition: number; isActive: boolean };
export type Hookah = { id: string; branchId: string; name: string; brand: string; model: string; status: string; photoUrl?: string | null; lastServiceAt?: string | null };
export type Bowl = { id: string; name: string; type: string; capacityGrams: number; recommendedStrength: string; averageSmokeMinutes: number; isActive: boolean };
export type Tobacco = { id: string; brand: string; line?: string | null; flavor: string; strength: string; category: string; description?: string | null; costPerGram: number; isActive: boolean; photoUrl?: string | null };
export type RuntimeOrder = { orderId: string; branchId: string; tableId: string; hookahId: string; clientId?: string | null; hookahMasterId?: string | null; status: string; totalPrice: number; nextCoalChangeAt?: string | null; updatedAt: string };
export type Booking = { id: string; clientId: string; branchId: string; tableId: string; startTime: string; endTime: string; guestsCount: number; status: string; depositAmount: number; comment?: string | null };
export type InventoryItem = { id: string; branchId: string; tobaccoId: string; stockGrams: number; minStockGrams: number; updatedAt: string };
export type InventoryMovement = { id: string; branchId: string; tobaccoId: string; type: string; amountGrams: number; reason?: string | null; orderId?: string | null; createdBy?: string | null; createdAt: string };
export type MixItem = { id: string; tobaccoId: string; percent: number; grams: number };
export type Mix = { id: string; name: string; description?: string | null; bowlId: string; strength: string; tasteProfile: string; totalGrams: number; price: number; cost?: number; margin?: number; isPublic: boolean; isActive: boolean; items?: MixItem[] };
export type StaffShift = { id: string; staffId: string; branchId: string; startsAt: string; endsAt: string; status: string; roleOnShift?: string | null; actualStartedAt?: string | null; actualFinishedAt?: string | null; cancelReason?: string | null };
export type DashboardMetrics = { revenue: number; ordersCount: number; averageCheck: number; bookingsCount: number; noShowRate: number; from?: string; to?: string; branchId?: string | null };
export type TopMixMetric = { mixId: string; name?: string; ordersCount: number; rating?: number; revenue: number };
export type TobaccoUsageMetric = { branchId: string; tobaccoId: string; grams?: number; amountGrams?: number };
export type StaffPerformanceMetric = { staffId: string; staffName: string; ordersServed: number; rating: number };
export type TableLoadMetric = { branchId: string; tableId: string; tableName: string; loadPercent: number };
export type Payment = { id: string; clientId: string; orderId?: string | null; bookingId?: string | null; originalAmount: number; discountAmount: number; payableAmount: number; refundedAmount: number; currency: string; provider: string; promocode?: string | null; externalPaymentId?: string | null; status: string; type: string; createdAt: string };

export type NotificationItem = { id: string; userId: string; channel: string; title: string; message: string; isRead: boolean; createdAt: string };
export type NotificationTemplate = { code: string; channel: string; title: string; message: string };
export type NotificationPreference = { userId: string; crmEnabled: boolean; telegramEnabled: boolean; smsEnabled: boolean; emailEnabled: boolean; pushEnabled: boolean };
export type Promocode = { id: string; code: string; discountType: string; discountValue: number; validFrom: string; validTo: string; maxRedemptions?: number | null; perClientLimit: number; isActive: boolean };
export type Review = { id: string; clientId: string; mixId?: string | null; orderId?: string | null; rating: number; text?: string | null; createdAt: string };
export type AnalyticsPoint = { label: string; value: number };

export const rolePermissions: Record<RoleCode, PermissionCode[]> = {
  OWNER: ["*"],
  MANAGER: ["branches.manage", "staff.manage", "mixes.manage", "inventory.manage", "orders.manage", "bookings.manage", "analytics.read"],
  HOOKAH_MASTER: ["mixes.manage", "inventory.manage", "orders.manage"],
  WAITER: ["orders.manage", "bookings.manage"],
  CLIENT: ["bookings.create"]
};

export function hasPermission(role: RoleCode | undefined, permission: PermissionCode) {
  if (!role) return false;
  const permissions = rolePermissions[role] ?? [];
  return permissions.includes("*") || permissions.includes(permission);
}

export function getTenants(accessToken?: string) {
  return getJson<Tenant[]>("/api/tenants", accessToken);
}

export function createTenant(payload: { name: string; slug: string }, accessToken?: string) {
  return postJson<Tenant>("/api/tenants", payload, accessToken);
}

export function updateTenant(id: string, payload: { name?: string; slug?: string; isActive?: boolean }, accessToken?: string) {
  return patchJson<Tenant>(`/api/tenants/${id}`, payload, accessToken);
}

export function getTenantSettings(id: string, accessToken?: string) {
  return getJson<TenantSettings>(`/api/tenants/${id}/settings`, accessToken);
}

export function updateTenantSettings(id: string, payload: TenantSettings, accessToken?: string) {
  return putJson<TenantSettings>(`/api/tenants/${id}/settings`, payload, accessToken);
}

export function getAuditLogs(query: { action?: string; targetType?: string; actorUserId?: string; limit?: number }, accessToken?: string) {
  const params = new URLSearchParams();
  if (query.action) params.set("action", query.action);
  if (query.targetType) params.set("targetType", query.targetType);
  if (query.actorUserId) params.set("actorUserId", query.actorUserId);
  if (query.limit) params.set("limit", String(query.limit));
  return getJson<AuditLog[]>(`/api/audit-logs?${params.toString()}`, accessToken);
}

export function getRoles(accessToken?: string) {
  return getJson<TenantRole[]>("/api/roles", accessToken);
}

export function getPermissions(accessToken?: string) {
  return getJson<PermissionDefinition[]>("/api/permissions", accessToken);
}

export function createRole(payload: { name: string; code: string }, accessToken?: string) {
  return postJson<TenantRole>("/api/roles", payload, accessToken);
}

export function updateRole(id: string, payload: { name?: string; isActive?: boolean }, accessToken?: string) {
  return patchJson<TenantRole>(`/api/roles/${id}`, payload, accessToken);
}

export function updateRolePermissions(id: string, permissions: string[], accessToken?: string) {
  return putJson<TenantRole>(`/api/roles/${id}/permissions`, { permissions }, accessToken);
}

export function deleteRole(id: string, accessToken?: string) {
  return deleteJson<void>(`/api/roles/${id}`, undefined, accessToken);
}

export function loginStaff(phone: string, password: string) {
  return sendJson<AuthResponse>("POST", "/api/auth/login", { phone, password });
}

export function refreshAuth(refreshToken: string) {
  return sendJson<AuthResponse>("POST", "/api/auth/refresh", { refreshToken });
}

export function logoutAuth(refreshToken: string) {
  return sendJson<void>("POST", "/api/auth/logout", { refreshToken });
}

type ApiAuthHooks = {
  getRefreshToken: () => string | undefined;
  onRefresh: (auth: AuthResponse) => void;
  onUnauthorized: () => void;
};

let authHooks: ApiAuthHooks | undefined;
let refreshInFlight: Promise<AuthResponse> | undefined;

export function configureApiAuth(hooks: ApiAuthHooks) {
  authHooks = hooks;
}

export function getJson<T>(path: string, accessToken?: string): Promise<T> {
  return sendJson<T>("GET", path, undefined, accessToken);
}

export function patchJson<T>(path: string, payload: unknown, accessToken?: string): Promise<T> {
  return sendJson<T>("PATCH", path, payload, accessToken);
}

export function putJson<T>(path: string, payload: unknown, accessToken?: string): Promise<T> {
  return sendJson<T>("PUT", path, payload, accessToken);
}

export function postJson<T>(path: string, payload: unknown, accessToken?: string): Promise<T> {
  return sendJson<T>("POST", path, payload, accessToken);
}

export function deleteJson<T>(path: string, payload?: unknown, accessToken?: string): Promise<T> {
  return sendJson<T>("DELETE", path, payload, accessToken);
}

export class ApiError extends Error {
  constructor(message: string, public readonly status: number) {
    super(message);
  }
}

async function sendJson<T>(method: "GET" | "POST" | "PATCH" | "PUT" | "DELETE", path: string, payload?: unknown, accessToken?: string): Promise<T> {
  const token = await getUsableToken(accessToken);
  const response = await request(method, path, payload, token);

  if (response.status === 401 && token) {
    const refreshed = await tryRefresh();
    if (refreshed) return readResponse<T>(await request(method, path, payload, refreshed.accessToken), method, path);
    authHooks?.onUnauthorized();
  }

  return readResponse<T>(response, method, path);
}

async function request(method: "GET" | "POST" | "PATCH" | "PUT" | "DELETE", path: string, payload?: unknown, accessToken?: string) {
  return fetch(`${API_BASE_URL}${path}`, {
    method,
    headers: {
      "Content-Type": "application/json",
      ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {})
    },
    body: payload === undefined ? undefined : JSON.stringify(payload)
  });
}

async function readResponse<T>(response: Response, method: string, path: string): Promise<T> {
  if (!response.ok) {
    const text = await response.text();
    throw new ApiError(readProblem(text) || `${method} ${path} failed with ${response.status}`, response.status);
  }

  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}

async function getUsableToken(accessToken?: string) {
  if (!accessToken || !tokenExpiresSoon(accessToken)) return accessToken;
  return (await tryRefresh())?.accessToken ?? accessToken;
}

async function tryRefresh() {
  const refreshToken = authHooks?.getRefreshToken();
  if (!refreshToken) return undefined;
  refreshInFlight ??= refreshAuth(refreshToken).finally(() => {
    refreshInFlight = undefined;
  });
  try {
    const auth = await refreshInFlight;
    authHooks?.onRefresh(auth);
    return auth;
  } catch {
    authHooks?.onUnauthorized();
    return undefined;
  }
}

function tokenExpiresSoon(token: string) {
  const payload = readJwtPayload(token);
  if (!payload?.exp) return false;
  return payload.exp * 1000 - Date.now() < 60_000;
}

function readJwtPayload(token: string): { exp?: number } | undefined {
  try {
    const [, payload] = token.split(".");
    if (!payload) return undefined;
    return JSON.parse(atob(payload.replace(/-/g, "+").replace(/_/g, "/"))) as { exp?: number };
  } catch {
    return undefined;
  }
}

function readProblem(text: string) {
  if (!text) return "";
  try {
    const parsed = JSON.parse(text) as { message?: string; Message?: string; title?: string; code?: string };
    return parsed.message ?? parsed.Message ?? parsed.title ?? parsed.code ?? text;
  } catch {
    return text;
  }
}

export function timeLabel(value?: string | null) {
  if (!value) return "--:--";
  return new Date(value).toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" });
}

export function shortId(value?: string | null) {
  return value ? value.slice(0, 8) : "-";
}
