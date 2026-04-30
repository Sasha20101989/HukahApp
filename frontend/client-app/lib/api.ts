const API_BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080";

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
};

export type RegisterPayload = {
  name: string;
  phone: string;
  email?: string;
  password: string;
};

export async function registerClient(payload: RegisterPayload) {
  return postJson<{ userId: string; accessToken: string; refreshToken: string }>("/api/auth/register", payload);
}

export async function loginClient(phone: string, password: string) {
  return postJson<{ accessToken: string; refreshToken: string }>("/api/auth/login", { phone, password });
}

export type CreatePaymentPayload = {
  clientId: string;
  bookingId: string;
  amount: number;
  currency: "RUB";
  type: "DEPOSIT";
  provider: "YOOKASSA";
};

export async function createBooking(payload: CreateBookingPayload) {
  return postJson<{ id: string; status: string }>("/api/bookings", payload);
}

export async function createDepositPayment(payload: CreatePaymentPayload) {
  return postJson<{ paymentId: string; paymentUrl: string }>("/api/payments/create", payload);
}

async function postJson<T>(path: string, payload: unknown): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json"
    },
    body: JSON.stringify(payload)
  });

  if (!response.ok) {
    const problem = await response.text();
    throw new Error(problem || `Request failed with ${response.status}`);
  }

  return response.json() as Promise<T>;
}
