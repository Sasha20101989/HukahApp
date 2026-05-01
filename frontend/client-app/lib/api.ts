const API_BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080";

export type AuthResponse = { userId: string; accessToken: string; refreshToken: string };
export type Branch = { id: string; name: string; address: string; phone: string; timezone: string };
export type Table = { id: string; name: string; capacity: number; status?: string };
export type Mix = { id: string; name: string; description?: string | null; bowlId: string; strength: string; tasteProfile: string; totalGrams: number; price: number; isPublic: boolean };
export type Booking = { id: string; clientId: string; branchId: string; tableId: string; startTime: string; endTime: string; guestsCount: number; status: string; depositAmount: number; comment?: string | null };
export type BookingHold = { id: string; branchId: string; tableId: string; clientId: string; startTime: string; endTime: string; guestsCount: number; expiresAt: string };
export type PaymentResponse = { paymentId: string; paymentUrl: string; amount: number; discount: number };
export type Review = { id: string; clientId: string; mixId?: string | null; orderId?: string | null; rating: number; text?: string | null; createdAt: string };

export const demoBranches: Branch[] = [{ id: "10000000-0000-0000-0000-000000000001", name: "Hookah Place Center", address: "Lenina, 1", phone: "+79990000000", timezone: "Europe/Moscow" }];
export const demoTables: Table[] = [
  { id: "30000000-0000-0000-0000-000000000001", name: "Стол 1", capacity: 4, status: "FREE" },
  { id: "30000000-0000-0000-0000-000000000002", name: "Стол 2", capacity: 6, status: "FREE" },
  { id: "30000000-0000-0000-0000-000000000003", name: "VIP 1", capacity: 8, status: "FREE" }
];
export const demoMixes: Mix[] = [
  { id: "70000000-0000-0000-0000-000000000001", name: "Berry Ice", description: "Ягоды, мята, средняя крепость", bowlId: "50000000-0000-0000-0000-000000000001", strength: "MEDIUM", tasteProfile: "BERRY_FRESH", totalGrams: 18, price: 850, isPublic: true },
  { id: "70000000-0000-0000-0000-000000000002", name: "Sweet Fresh", description: "Сладкий холодный микс", bowlId: "50000000-0000-0000-0000-000000000001", strength: "LIGHT", tasteProfile: "SWEET_FRESH", totalGrams: 18, price: 820, isPublic: true },
  { id: "70000000-0000-0000-0000-000000000003", name: "Dark Citrus", description: "Плотный цитрус", bowlId: "50000000-0000-0000-0000-000000000001", strength: "STRONG", tasteProfile: "CITRUS", totalGrams: 20, price: 920, isPublic: true }
];
export const demoReviews: Review[] = [
  { id: "r1", clientId: "demo", mixId: demoMixes[0].id, rating: 5, text: "Ягодный микс отлично зашел", createdAt: new Date().toISOString() },
  { id: "r2", clientId: "demo", mixId: demoMixes[1].id, rating: 4, text: "Свежий и легкий", createdAt: new Date().toISOString() }
];

export async function getJson<T>(path: string, fallback: T, accessToken?: string): Promise<T> {
  try {
    const response = await fetch(`${API_BASE_URL}${path}`, { headers: headers(accessToken) });
    if (!response.ok) throw new Error(`GET ${path} failed with ${response.status}`);
    return response.json() as Promise<T>;
  } catch {
    return fallback;
  }
}

export function registerClient(payload: { name: string; phone: string; email?: string; password: string }) {
  return postJson<AuthResponse>("/api/auth/register", payload);
}

export function loginClient(phone: string, password: string) {
  return postJson<AuthResponse>("/api/auth/login", { phone, password });
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

export function createReview(payload: { clientId: string; orderId?: string; mixId?: string; rating: number; text: string }, accessToken?: string) {
  return postJson<Review>("/api/reviews", payload, accessToken);
}

export type CreateBookingPayload = {
  branchId: string;
  tableId: string;
  clientId: string;
  startTime: string;
  endTime: string;
  guestsCount: number;
  hookahId: string;
  bowlId: string;
  mixId: string;
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
};

async function postJson<T>(path: string, payload: unknown, accessToken?: string): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    method: "POST",
    headers: headers(accessToken),
    body: JSON.stringify(payload)
  });

  if (!response.ok) {
    const problem = await response.text();
    throw new Error(problem || `POST ${path} failed with ${response.status}`);
  }

  return response.json() as Promise<T>;
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
