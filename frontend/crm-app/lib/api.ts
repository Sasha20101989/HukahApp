const API_BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080";

export type Branch = { id: string; name: string; address: string; phone: string; timezone: string; isActive: boolean };
export type FloorPlan = { branchId: string; halls: Hall[]; zones: Zone[]; tables: Table[] };
export type Hall = { id: string; branchId: string; name: string; description?: string };
export type Zone = { id: string; branchId: string; name: string; color?: string; isActive: boolean };
export type Table = { id: string; hallId: string; zoneId?: string | null; name: string; capacity: number; status: string; xPosition: number; yPosition: number; isActive: boolean };
export type Hookah = { id: string; branchId: string; name: string; brand: string; model: string; status: string };
export type RuntimeOrder = { orderId: string; branchId: string; tableId: string; hookahId: string; clientId?: string | null; hookahMasterId?: string | null; status: string; totalPrice: number; nextCoalChangeAt?: string | null; updatedAt: string };
export type Order = { id: string; branchId: string; tableId: string; clientId?: string | null; hookahMasterId?: string | null; waiterId?: string | null; status: string; totalPrice: number; createdAt: string; servedAt?: string | null; completedAt?: string | null; nextCoalChangeAt?: string | null; items: OrderItem[] };
export type OrderItem = { id: string; hookahId: string; bowlId: string; mixId: string; price: number; status: string };
export type Booking = { id: string; clientId: string; branchId: string; tableId: string; startTime: string; endTime: string; guestsCount: number; status: string; depositAmount: number; comment?: string | null };
export type InventoryItem = { id: string; branchId: string; tobaccoId: string; stockGrams: number; minStockGrams: number; updatedAt: string };
export type InventoryMovement = { id: string; branchId: string; tobaccoId: string; type: string; amountGrams: number; reason?: string | null; orderId?: string | null; createdAt: string };
export type Mix = { id: string; name: string; description?: string | null; bowlId: string; strength: string; tasteProfile: string; totalGrams: number; price: number; cost?: number; margin?: number; isPublic: boolean; isActive: boolean };
export type StaffShift = { id: string; staffId: string; branchId: string; startsAt: string; endsAt: string; status: string; roleOnShift?: string | null };
export type DashboardMetrics = { revenue: number; ordersCount: number; averageCheck: number; bookingsCount: number; noShowRate: number };

export const demoBranch: Branch = { id: "10000000-0000-0000-0000-000000000001", name: "Hookah Place Center", address: "Lenina, 1", phone: "+79990000000", timezone: "Europe/Moscow", isActive: true };

export const demoFloorPlan: FloorPlan = {
  branchId: demoBranch.id,
  halls: [{ id: "20000000-0000-0000-0000-000000000001", branchId: demoBranch.id, name: "Main hall" }],
  zones: [{ id: "21000000-0000-0000-0000-000000000001", branchId: demoBranch.id, name: "Main zone", color: "#2f7d6d", isActive: true }],
  tables: [
    { id: "30000000-0000-0000-0000-000000000001", hallId: "20000000-0000-0000-0000-000000000001", zoneId: "21000000-0000-0000-0000-000000000001", name: "Стол 1", capacity: 4, status: "OCCUPIED", xPosition: 120, yPosition: 300, isActive: true },
    { id: "30000000-0000-0000-0000-000000000002", hallId: "20000000-0000-0000-0000-000000000001", zoneId: "21000000-0000-0000-0000-000000000001", name: "Стол 2", capacity: 6, status: "FREE", xPosition: 300, yPosition: 180, isActive: true },
    { id: "30000000-0000-0000-0000-000000000003", hallId: "20000000-0000-0000-0000-000000000001", zoneId: "21000000-0000-0000-0000-000000000001", name: "VIP 1", capacity: 8, status: "FREE", xPosition: 520, yPosition: 330, isActive: true }
  ]
};

export const demoRuntimeOrders: RuntimeOrder[] = [
  { orderId: "80000000-0000-0000-0000-000000000001", branchId: demoBranch.id, tableId: demoFloorPlan.tables[0].id, hookahId: "40000000-0000-0000-0000-000000000001", status: "SMOKING", totalPrice: 850, nextCoalChangeAt: new Date(Date.now() + 13 * 60_000).toISOString(), updatedAt: new Date().toISOString() },
  { orderId: "80000000-0000-0000-0000-000000000002", branchId: demoBranch.id, tableId: demoFloorPlan.tables[1].id, hookahId: "40000000-0000-0000-0000-000000000002", status: "PREPARING", totalPrice: 920, updatedAt: new Date().toISOString() }
];

export const demoInventory: InventoryItem[] = [
  { id: "1", branchId: demoBranch.id, tobaccoId: "Darkside Strawberry", stockGrams: 42, minStockGrams: 50, updatedAt: new Date().toISOString() },
  { id: "2", branchId: demoBranch.id, tobaccoId: "Musthave Mint", stockGrams: 118, minStockGrams: 50, updatedAt: new Date().toISOString() },
  { id: "3", branchId: demoBranch.id, tobaccoId: "Element Blueberry", stockGrams: 74, minStockGrams: 50, updatedAt: new Date().toISOString() }
];

export const demoBookings: Booking[] = [
  { id: "b1", clientId: "Александр", branchId: demoBranch.id, tableId: demoFloorPlan.tables[1].id, startTime: new Date(Date.now() + 90 * 60_000).toISOString(), endTime: new Date(Date.now() + 210 * 60_000).toISOString(), guestsCount: 4, status: "CONFIRMED", depositAmount: 2000, comment: "День рождения" },
  { id: "b2", clientId: "Екатерина", branchId: demoBranch.id, tableId: demoFloorPlan.tables[2].id, startTime: new Date(Date.now() + 150 * 60_000).toISOString(), endTime: new Date(Date.now() + 270 * 60_000).toISOString(), guestsCount: 6, status: "WAITING_PAYMENT", depositAmount: 3000 }
];

export const demoMixes: Mix[] = [
  { id: "70000000-0000-0000-0000-000000000001", name: "Berry Ice", description: "Ягодно-свежий микс", bowlId: "50000000-0000-0000-0000-000000000001", strength: "MEDIUM", tasteProfile: "BERRY_FRESH", totalGrams: 18, price: 850, cost: 126, margin: 724, isPublic: true, isActive: true },
  { id: "70000000-0000-0000-0000-000000000002", name: "Dark Citrus", description: "Плотный цитрус", bowlId: "50000000-0000-0000-0000-000000000001", strength: "STRONG", tasteProfile: "CITRUS", totalGrams: 20, price: 920, cost: 130, margin: 790, isPublic: true, isActive: true }
];

export const demoMetrics: DashboardMetrics = { revenue: 450000, ordersCount: 320, averageCheck: 1406, bookingsCount: 120, noShowRate: 8.5 };

export async function getJson<T>(path: string, fallback: T): Promise<T> {
  try {
    const response = await fetch(`${API_BASE_URL}${path}`, { headers: { "Content-Type": "application/json" } });
    if (!response.ok) throw new Error(`GET ${path} failed with ${response.status}`);
    return response.json() as Promise<T>;
  } catch {
    return fallback;
  }
}

export async function patchJson<T>(path: string, payload: unknown): Promise<T> {
  return sendJson<T>("PATCH", path, payload);
}

export async function postJson<T>(path: string, payload: unknown): Promise<T> {
  return sendJson<T>("POST", path, payload);
}

async function sendJson<T>(method: "POST" | "PATCH" | "DELETE", path: string, payload?: unknown): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    method,
    headers: { "Content-Type": "application/json" },
    body: payload === undefined ? undefined : JSON.stringify(payload)
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `${method} ${path} failed with ${response.status}`);
  }

  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}

export function timeLabel(value?: string | null) {
  if (!value) return "--:--";
  return new Date(value).toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" });
}

export function shortId(value?: string | null) {
  return value ? value.slice(0, 8) : "-";
}
