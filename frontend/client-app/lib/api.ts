const API_BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080";

export type AuthResponse = { userId: string; accessToken: string; refreshToken: string };
export type UserProfile = { id: string; name: string; phone: string; email?: string | null; role: string; branchId?: string | null; status: string };
export type Branch = { id: string; name: string; address: string; phone: string; timezone: string };
export type TenantBranding = { tenantId: string; name: string; logoUrl?: string | null; primaryColor: string; accentColor: string };
export type Table = { id: string; name: string; capacity: number; status?: string };
export type Mix = { id: string; name: string; description?: string | null; bowlId: string; strength: string; tasteProfile: string; totalGrams: number; price: number; isPublic: boolean };
export type Booking = { id: string; clientId: string; branchId: string; tableId: string; startTime: string; endTime: string; guestsCount: number; status: string; depositAmount: number; comment?: string | null };
export type BookingHold = { id: string; branchId: string; tableId: string; clientId: string; startTime: string; endTime: string; guestsCount: number; expiresAt: string };
export type PaymentResponse = { paymentId: string; paymentUrl: string; returnUrl: string; checkoutMode: "PROVIDER_REDIRECT" | "LOCAL_RETURN"; status: string; amount: number; discount: number };
export type PaymentStatus = { id: string; bookingId?: string | null; orderId?: string | null; status: string; type: string; provider: string; payableAmount: number; refundedAmount: number; currency: string; createdAt: string };
export type Review = { id: string; clientId: string; mixId?: string | null; orderId?: string | null; rating: number; text?: string | null; createdAt: string };
export type NotificationPreference = { userId: string; crmEnabled: boolean; telegramEnabled: boolean; smsEnabled: boolean; emailEnabled: boolean; pushEnabled: boolean };

export class ApiError extends Error {
  constructor(message: string, public readonly status: number) {
    super(message);
  }
}

export async function getJson<T>(path: string, accessToken?: string): Promise<T> {
  return sendJson<T>("GET", path, undefined, accessToken);
}

export function getTenantBranding() {
  return getJson<TenantBranding>("/api/public/tenant/branding");
}

export function registerClient(payload: { name: string; phone: string; email?: string; password: string }) {
  return postJson<AuthResponse>("/api/auth/register", payload);
}

export function loginClient(phone: string, password: string) {
  return postJson<AuthResponse>("/api/auth/login", { phone, password });
}

export function refreshAuth(refreshToken: string) {
  return postJson<AuthResponse>("/api/auth/refresh", { refreshToken });
}

export function logoutAuth(refreshToken: string) {
  return postJson<void>("/api/auth/logout", { refreshToken });
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

export function updateClientProfile(userId: string, payload: { name: string; email?: string }, accessToken?: string) {
  return patchJson<UserProfile>(`/api/users/${userId}`, payload, accessToken);
}

export function getNotificationPreference(userId: string, accessToken?: string) {
  return getJson<NotificationPreference>(`/api/notifications/preferences/${userId}`, accessToken);
}

export function updateNotificationPreference(userId: string, payload: Omit<NotificationPreference, "userId">, accessToken?: string) {
  return putJson<NotificationPreference>(`/api/notifications/preferences/${userId}`, payload, accessToken);
}

export function createHold(payload: { branchId: string; tableId: string; clientId: string; startTime: string; endTime: string; guestsCount: number }, accessToken?: string) {
  return postJson<BookingHold>("/api/bookings/holds", payload, accessToken);
}

export function createBooking(payload: CreateBookingPayload, accessToken?: string) {
  return postJson<Booking>("/api/bookings", payload, accessToken);
}

export function createDepositPayment(payload: CreatePaymentPayload, accessToken?: string) {
  return postJson<PaymentResponse>("/api/payments/create", payload, accessToken);
}

export function getPaymentStatus(paymentId: string) {
  return getJson<PaymentStatus>(`/api/payments/status/${paymentId}`);
}

export function createReview(payload: { clientId: string; orderId?: string; mixId?: string; rating: number; text: string }, accessToken?: string) {
  return postJson<Review>("/api/reviews", payload, accessToken);
}

export function updateReview(reviewId: string, payload: { rating?: number; text?: string }, accessToken?: string) {
  return patchJson<Review>(`/api/reviews/${reviewId}`, payload, accessToken);
}

export function deleteReview(reviewId: string, accessToken?: string) {
  return deleteJson<void>(`/api/reviews/${reviewId}`, undefined, accessToken);
}

export type CreateBookingPayload = {
  branchId: string;
  tableId: string;
  clientId: string;
  startTime: string;
  endTime: string;
  guestsCount: number;
  hookahId?: string;
  bowlId?: string;
  mixId?: string;
  comment?: string;
  depositAmount: number;
  holdId?: string;
};

export type CreatePaymentPayload = {
  clientId: string;
  bookingId: string;
  amount: number;
  currency: "RUB";
  type: "DEPOSIT";
  provider: "YOOKASSA";
  promocode?: string;
  returnUrl?: string;
};

async function postJson<T>(path: string, payload: unknown, accessToken?: string): Promise<T> {
  return sendJson<T>("POST", path, payload, accessToken);
}

async function putJson<T>(path: string, payload: unknown, accessToken?: string): Promise<T> {
  return sendJson<T>("PUT", path, payload, accessToken);
}

async function patchJson<T>(path: string, payload: unknown, accessToken?: string): Promise<T> {
  return sendJson<T>("PATCH", path, payload, accessToken);
}

async function deleteJson<T>(path: string, payload?: unknown, accessToken?: string): Promise<T> {
  return sendJson<T>("DELETE", path, payload, accessToken);
}

async function sendJson<T>(method: "GET" | "POST" | "PUT" | "PATCH" | "DELETE", path: string, payload?: unknown, accessToken?: string): Promise<T> {
  const token = await getUsableToken(accessToken);
  const response = await request(method, path, payload, token);

  if (response.status === 401 && token) {
    const refreshed = await tryRefresh();
    if (refreshed) return readResponse<T>(await request(method, path, payload, refreshed.accessToken), method, path);
    authHooks?.onUnauthorized();
  }

  return readResponse<T>(response, method, path);
}

async function request(method: "GET" | "POST" | "PUT" | "PATCH" | "DELETE", path: string, payload?: unknown, accessToken?: string) {
  return fetch(`${API_BASE_URL}${path}`, {
    method,
    headers: headers(accessToken),
    body: payload === undefined ? undefined : JSON.stringify(payload)
  });
}

async function readResponse<T>(response: Response, method: string, path: string): Promise<T> {
  if (!response.ok) {
    const problem = await response.text();
    throw new ApiError(readProblem(problem) || `${method} ${path} failed with ${response.status}`, response.status);
  }

  if (response.status === 204) return undefined as T;
  const text = await response.text();
  if (!text) return undefined as T;
  return JSON.parse(text) as T;
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

function headers(accessToken?: string) {
  return {
    "Content-Type": "application/json",
    ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {})
  };
}

export function buildBookingWindow(date: string, time: string) {
  const start = new Date(`${date}T${time}:00.000Z`);
  const end = new Date(start.getTime() + 2 * 60 * 60 * 1000);
  return { startTime: start.toISOString(), endTime: end.toISOString() };
}

export function timeLabel(value: string) {
  return new Date(value).toLocaleString("ru-RU", { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" });
}
