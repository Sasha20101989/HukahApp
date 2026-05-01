"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CalendarClock, ClipboardList, Flame, Gauge, LayoutDashboard, PackageSearch, Plus, Search, ShieldCheck, TimerReset, UsersRound, WifiOff } from "lucide-react";
import { useMemo, useState } from "react";
import { demoBookings, demoBranch, demoFloorPlan, demoInventory, demoMetrics, demoMixes, demoRuntimeOrders, getJson, patchJson, postJson, shortId, timeLabel, type Booking, type FloorPlan, type InventoryItem, type Mix, type RuntimeOrder } from "../lib/api";
import { useCrmStore, type CrmSection } from "../lib/store";

const sections: Array<{ id: CrmSection; label: string; icon: React.ReactNode; roles: string[] }> = [
  { id: "floor", label: "Зал", icon: <LayoutDashboard size={18} />, roles: ["OWNER", "MANAGER", "HOOKAH_MASTER", "WAITER"] },
  { id: "orders", label: "Заказы", icon: <ClipboardList size={18} />, roles: ["OWNER", "MANAGER", "HOOKAH_MASTER", "WAITER"] },
  { id: "bookings", label: "Брони", icon: <CalendarClock size={18} />, roles: ["OWNER", "MANAGER", "WAITER"] },
  { id: "inventory", label: "Склад", icon: <PackageSearch size={18} />, roles: ["OWNER", "MANAGER", "HOOKAH_MASTER"] },
  { id: "mixology", label: "Миксы", icon: <Flame size={18} />, roles: ["OWNER", "MANAGER", "HOOKAH_MASTER"] },
  { id: "staff", label: "Персонал", icon: <UsersRound size={18} />, roles: ["OWNER", "MANAGER"] }
];

export default function CrmDashboard() {
  const queryClient = useQueryClient();
  const { branchId, role, section, search, setRole, setSection, setSearch } = useCrmStore();
  const [toast, setToast] = useState("");

  const branches = useQuery({ queryKey: ["branches"], queryFn: () => getJson("/api/branches", [demoBranch]) });
  const floorPlan = useQuery({ queryKey: ["floor-plan", branchId], queryFn: () => getJson<FloorPlan>(`/api/branches/${branchId}/floor-plan`, demoFloorPlan) });
  const runtimeOrders = useQuery({ queryKey: ["runtime-orders", branchId], queryFn: () => getJson<RuntimeOrder[]>(`/api/orders/runtime/branch/${branchId}`, demoRuntimeOrders) });
  const bookings = useQuery({ queryKey: ["bookings", branchId], queryFn: () => getJson<Booking[]>(`/api/bookings?branchId=${branchId}`, demoBookings) });
  const inventory = useQuery({ queryKey: ["inventory", branchId], queryFn: () => getJson<InventoryItem[]>(`/api/inventory?branchId=${branchId}&lowStockOnly=false`, demoInventory) });
  const mixes = useQuery({ queryKey: ["mixes"], queryFn: () => getJson<Mix[]>("/api/mixes?publicOnly=false", demoMixes) });
  const metrics = useQuery({ queryKey: ["metrics", branchId], queryFn: () => getJson(`/api/analytics/dashboard?branchId=${branchId}&from=2026-04-01&to=2026-04-30`, demoMetrics) });

  const statusMutation = useMutation({
    mutationFn: ({ orderId, status }: { orderId: string; status: string }) => patchJson(`/api/orders/${orderId}/status`, { status }),
    onSuccess: async () => {
      setToast("Статус заказа обновлен");
      await Promise.all([queryClient.invalidateQueries({ queryKey: ["runtime-orders"] }), queryClient.invalidateQueries({ queryKey: ["floor-plan"] })]);
    },
    onError: (error) => setToast(error instanceof Error ? error.message : "Не удалось обновить статус")
  });

  const coalMutation = useMutation({
    mutationFn: (orderId: string) => postJson(`/api/orders/${orderId}/coal-change`, { changedAt: new Date().toISOString() }),
    onSuccess: async () => {
      setToast("Замена углей зафиксирована");
      await queryClient.invalidateQueries({ queryKey: ["runtime-orders"] });
    },
    onError: (error) => setToast(error instanceof Error ? error.message : "Не удалось отметить угли")
  });

  const tables = floorPlan.data?.tables ?? [];
  const activeOrders = runtimeOrders.data ?? [];
  const filteredOrders = useMemo(() => activeOrders.filter((order) => `${order.orderId} ${order.status} ${order.tableId}`.toLowerCase().includes(search.toLowerCase())), [activeOrders, search]);
  const allowedSections = sections.filter((item) => item.roles.includes(role));
  const branchName = branches.data?.find((branch) => branch.id === branchId)?.name ?? demoBranch.name;
  const isOfflineFallback = floorPlan.data === demoFloorPlan || runtimeOrders.data === demoRuntimeOrders;

  return (
    <main className="shell">
      <aside className="sidebar">
        <div className="brand"><span className="brand-mark"><Flame size={20} /></span><span>Hookah CRM</span></div>
        <div className="role-card">
          <span><ShieldCheck size={15} /> Роль смены</span>
          <select value={role} onChange={(event) => setRole(event.target.value as never)}>
            <option value="OWNER">Owner</option>
            <option value="MANAGER">Manager</option>
            <option value="HOOKAH_MASTER">Hookah master</option>
            <option value="WAITER">Waiter</option>
          </select>
        </div>
        <nav className="nav" aria-label="CRM sections">
          {allowedSections.map((item) => (
            <button className={section === item.id ? "active" : ""} onClick={() => setSection(item.id)} key={item.id}>{item.icon}{item.label}</button>
          ))}
        </nav>
      </aside>

      <section className="main">
        <header className="topbar">
          <div className="title">
            <h1>Операционный экран</h1>
            <p>{branchName} · очередь, зал, склад, брони и персонал для планшета.</p>
          </div>
          <div className="toolbar">
            {isOfflineFallback && <span className="offline"><WifiOff size={15} /> demo data</span>}
            <label className="search"><Search size={16} /><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Поиск" /></label>
            <button className="primary"><Plus size={18} />Новый заказ</button>
          </div>
        </header>

        <div className="kpi-grid">
          <Kpi label="Выручка" value={`${Math.round(metrics.data?.revenue ?? 0).toLocaleString("ru-RU")} ₽`} />
          <Kpi label="Заказы" value={String(metrics.data?.ordersCount ?? activeOrders.length)} />
          <Kpi label="Средний чек" value={`${Math.round(metrics.data?.averageCheck ?? 0)} ₽`} />
          <Kpi label="No-show" value={`${metrics.data?.noShowRate ?? 0}%`} tone="warn" />
        </div>

        {section === "floor" && <FloorSection tables={tables} orders={filteredOrders} onStatus={(orderId, status) => statusMutation.mutate({ orderId, status })} onCoal={(orderId) => coalMutation.mutate(orderId)} />}
        {section === "orders" && <OrdersSection orders={filteredOrders} tables={tables} onStatus={(orderId, status) => statusMutation.mutate({ orderId, status })} onCoal={(orderId) => coalMutation.mutate(orderId)} />}
        {section === "bookings" && <BookingsSection bookings={bookings.data ?? []} tables={tables} />}
        {section === "inventory" && <InventorySection inventory={inventory.data ?? []} />}
        {section === "mixology" && <MixologySection mixes={mixes.data ?? []} />}
        {section === "staff" && <StaffSection />}

        {toast && <button className="toast" onClick={() => setToast("")}>{toast}</button>}
      </section>
    </main>
  );
}

function Kpi({ label, value, tone }: { label: string; value: string; tone?: "warn" }) {
  return <article className={`kpi ${tone ?? ""}`}><span>{label}</span><strong>{value}</strong></article>;
}

function FloorSection({ tables, orders, onStatus, onCoal }: { tables: FloorPlan["tables"]; orders: RuntimeOrder[]; onStatus: (orderId: string, status: string) => void; onCoal: (orderId: string) => void }) {
  return (
    <div className="dashboard">
      <section className="section wide">
        <div className="section-head"><h2>Схема зала</h2><span className="meta">live resources</span></div>
        <div className="floor">
          {tables.map((table) => {
            const order = orders.find((candidate) => candidate.tableId === table.id);
            return (
              <button className={`table-dot ${table.status !== "FREE" || order ? "busy" : ""}`} style={{ left: `${Math.min(78, Number(table.xPosition) / 7)}%`, top: `${Math.min(78, Number(table.yPosition) / 6)}%` }} key={table.id}>
                <strong>{table.name}</strong>
                <span>{order?.status ?? table.status}</span>
              </button>
            );
          })}
        </div>
      </section>
      <section className="section"><OrderQueue orders={orders} onStatus={onStatus} onCoal={onCoal} /></section>
    </div>
  );
}

function OrdersSection(props: { orders: RuntimeOrder[]; tables: FloorPlan["tables"]; onStatus: (orderId: string, status: string) => void; onCoal: (orderId: string) => void }) {
  return <section className="section"><OrderQueue {...props} /></section>;
}

function OrderQueue({ orders, onStatus, onCoal }: { orders: RuntimeOrder[]; onStatus: (orderId: string, status: string) => void; onCoal: (orderId: string) => void }) {
  return (
    <>
      <div className="section-head"><h2>Активные заказы</h2><span className="meta">{orders.length} в работе</span></div>
      <div className="queue cards">
        {orders.map((order) => (
          <article className="order-card" key={order.orderId}>
            <div className="order-top"><strong>#{shortId(order.orderId)} · стол {shortId(order.tableId)}</strong><span className={`status ${order.status === "SMOKING" ? "ok" : "warn"}`}>{order.status}</span></div>
            <div className="meta">Кальян {shortId(order.hookahId)} · {order.totalPrice} ₽</div>
            <div className="coal"><TimerReset size={16} />Следующие угли: {timeLabel(order.nextCoalChangeAt)}</div>
            <div className="actions">
              <button onClick={() => onStatus(order.orderId, "READY")}>Готов</button>
              <button onClick={() => onStatus(order.orderId, "SERVED")}>Вынесен</button>
              <button onClick={() => onCoal(order.orderId)}>Угли</button>
              <button onClick={() => onStatus(order.orderId, "COMPLETED")}>Закрыть</button>
            </div>
          </article>
        ))}
      </div>
    </>
  );
}

function BookingsSection({ bookings, tables }: { bookings: Booking[]; tables: FloorPlan["tables"] }) {
  return <section className="section"><div className="section-head"><h2>Бронирования</h2><span className="meta">депозиты и no-show</span></div><div className="queue cards">{bookings.map((booking) => <article className="booking-row" key={booking.id}><div><strong>{timeLabel(booking.startTime)} · {shortId(booking.clientId)}</strong><div className="meta">{tables.find((table) => table.id === booking.tableId)?.name ?? shortId(booking.tableId)} · {booking.guestsCount} гостей · депозит {booking.depositAmount} ₽</div></div><span className={`status ${booking.status === "CONFIRMED" ? "ok" : "warn"}`}>{booking.status}</span></article>)}</div></section>;
}

function InventorySection({ inventory }: { inventory: InventoryItem[] }) {
  return <section className="section"><div className="section-head"><h2>Склад</h2><span className="meta">low-stock контроль</span></div><div className="inventory-grid">{inventory.map((item) => <article className="stock-card" key={item.id}><strong>{item.tobaccoId}</strong><div className="bar"><span style={{ width: `${Math.min(100, (item.stockGrams / Math.max(1, item.minStockGrams * 3)) * 100)}%` }} /></div><div className="stock-line"><span>{item.stockGrams} г</span><span>min {item.minStockGrams} г</span></div></article>)}</div></section>;
}

function MixologySection({ mixes }: { mixes: Mix[] }) {
  return <section className="section"><div className="section-head"><h2>Миксология</h2><span className="meta">себестоимость скрывать в client app</span></div><div className="mix-grid">{mixes.map((mix) => <article className="mix-card" key={mix.id}><strong>{mix.name}</strong><p>{mix.description ?? mix.tasteProfile}</p><div className="pill-row"><span>{mix.strength}</span><span>{mix.totalGrams} г</span><span>{mix.price} ₽</span><span>маржа {mix.margin ?? "-"}</span></div></article>)}</div></section>;
}

function StaffSection() {
  return <section className="section staff-board"><div><Gauge size={22} /><h2>Смена персонала</h2><p>Здесь рабочий планшет менеджера: роли, активные смены, эффективность кальянщиков и быстрый доступ к назначению заказа.</p></div><div className="staff-grid"><article><strong>Hookah master</strong><span>3 заказа · avg 12 мин</span></article><article><strong>Waiter</strong><span>5 броней · 0 no-show</span></article><article><strong>Manager</strong><span>низкие остатки: 1</span></article></div></section>;
}
