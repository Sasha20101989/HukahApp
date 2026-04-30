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
  return postJson<{ userId: string; accessToken: string; refreshToken: string }>("/api/auth/login", { phone, password });
}

export type CreatePaymentPayload = {
  clientId: string;
  bookingId: string;
  amount: number;
  currency: "RUB";
  type: "DEPOSIT";
  provider: "YOOKASSA";
  promocode?: string;
};

export async function createBooking(payload: CreateBookingPayload, accessToken: string) {
  return postJson<{ id: string; status: string }>("/api/bookings", payload, accessToken);
}

export async function createDepositPayment(payload: CreatePaymentPayload, accessToken: string) {
  return postJson<{ paymentId: string; paymentUrl: string; amount: number; discount: number }>("/api/payments/create", payload, accessToken);
}

async function postJson<T>(path: string, payload: unknown, accessToken?: string): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {})
    },
    body: JSON.stringify(payload)
  });

  if (!response.ok) {
    const problem = await response.text();
    throw new Error(problem || `Request failed with ${response.status}`);
  }

  return response.json() as Promise<T>;
}
