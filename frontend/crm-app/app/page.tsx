"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { BarChart3, Bell, Building2, CalendarClock, ClipboardList, Flame, Gauge, LayoutDashboard, LockKeyhole, LogOut, MessageSquare, PackageSearch, Plus, Search, ShieldCheck, Tags, TimerReset, UsersRound } from "lucide-react";
import type { DragEvent, ReactNode } from "react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { configureApiAuth, deleteJson, getJson, hasPermission, loginStaff, logoutAuth, patchJson, postJson, putJson, shortId, timeLabel, type Booking, type Bowl, type Branch, type BranchWorkingHours, type DashboardMetrics, type FloorPlan, type Hookah, type InventoryItem, type InventoryMovement, type Mix, type NotificationItem, type NotificationPreference, type NotificationTemplate, type Payment, type Promocode, type Review, type RoleCode, type RuntimeOrder, type StaffPerformanceMetric, type StaffShift, type Table, type TableLoadMetric, type Tobacco, type TobaccoUsageMetric, type TopMixMetric, type UserProfile } from "../lib/api";
import { hydrateCrmSession, useCrmStore, type CrmSection } from "../lib/store";
import { ActionButton, CrudRowActions, CrudToolbar, EmptyState, FieldError, FormError, FormField, LoadingState, MutationToast } from "../lib/ui";
import { isValidEmail, isValidPhone, normalizePhone, normalizePhoneInput } from "../lib/validation";

const sections: Array<{ id: CrmSection; label: string; icon: React.ReactNode; permission?: Parameters<typeof hasPermission>[1] }> = [
  { id: "floor", label: "Зал", icon: <LayoutDashboard size={18} />, permission: "orders.manage" },
  { id: "orders", label: "Заказы", icon: <ClipboardList size={18} />, permission: "orders.manage" },
  { id: "bookings", label: "Брони", icon: <CalendarClock size={18} />, permission: "bookings.manage" },
  { id: "inventory", label: "Склад", icon: <PackageSearch size={18} />, permission: "inventory.manage" },
  { id: "mixology", label: "Миксы", icon: <Flame size={18} />, permission: "mixes.manage" },
  { id: "staff", label: "Персонал", icon: <UsersRound size={18} />, permission: "staff.manage" },
  { id: "analytics", label: "Аналитика", icon: <BarChart3 size={18} />, permission: "analytics.read" },
  { id: "notifications", label: "Уведомления", icon: <Bell size={18} />, permission: "bookings.manage" },
  { id: "reviews", label: "Отзывы", icon: <MessageSquare size={18} />, permission: "orders.manage" },
  { id: "promo", label: "Промо", icon: <Tags size={18} />, permission: "orders.manage" }
];

export default function CrmDashboard() {
  const queryClient = useQueryClient();
  const { branchId, section, search, session, hydrated, setAuth, setProfile, setBranchId, setSection, setSearch, logout } = useCrmStore();
  const [toast, setToast] = useState("");
  const [analyticsFromDate, setAnalyticsFromDate] = useState(() => defaultDateInput(-30));
  const [analyticsToDate, setAnalyticsToDate] = useState(() => defaultDateInput());
  const token = session.accessToken;
  const role = session.profile?.role;

  useEffect(() => hydrateCrmSession(), []);

  useEffect(() => configureApiAuth({
    getRefreshToken: () => useCrmStore.getState().session.refreshToken,
    onRefresh: setAuth,
    onUnauthorized: logout
  }), [logout, setAuth]);

  const authorized = useCallback(async <T,>(call: (accessToken: string) => Promise<T>): Promise<T> => {
    if (!token) throw new Error("Сессия не найдена.");
    return call(token);
  }, [token]);

  const handleLogout = useCallback(async () => {
    if (session.refreshToken) {
      try { await logoutAuth(session.refreshToken); } catch { /* local logout still wins */ }
    }
    logout();
  }, [logout, session.refreshToken]);

  const profile = useQuery({
    queryKey: ["me", token],
    enabled: Boolean(token),
    queryFn: () => authorized((accessToken) => getJson<UserProfile>("/api/users/me", accessToken)),
    retry: false
  });

  useEffect(() => {
    if (profile.data) setProfile(profile.data);
  }, [profile.data, setProfile]);

  const allowedSections = sections.filter((item) => !item.permission || hasPermission(role, item.permission));
  useEffect(() => {
    if (role && allowedSections.length > 0 && !allowedSections.some((item) => item.id === section)) {
      setSection(allowedSections[0].id);
    }
  }, [role, section, allowedSections, setSection]);

  const canUseApp = hydrated && Boolean(token) && Boolean(role) && !profile.isError;
  const branchScoped = canUseApp && Boolean(branchId);
  const branches = useQuery({ queryKey: ["branches"], enabled: canUseApp, queryFn: () => authorized((accessToken) => getJson<Branch[]>("/api/branches", accessToken)) });
  const analyticsRangeValid = Boolean(analyticsFromDate && analyticsToDate && analyticsFromDate <= analyticsToDate);
  const analyticsQuery = useMemo(() => new URLSearchParams({ branchId, from: analyticsFromDate, to: analyticsToDate }).toString(), [analyticsFromDate, analyticsToDate, branchId]);

  const floorPlan = useQuery({ queryKey: ["floor-plan", branchId], enabled: branchScoped, queryFn: () => authorized((accessToken) => getJson<FloorPlan>(`/api/branches/${branchId}/floor-plan`, accessToken)) });
  const workingHours = useQuery({ queryKey: ["working-hours", branchId], enabled: branchScoped && hasPermission(role, "branches.manage"), queryFn: () => authorized((accessToken) => getJson<BranchWorkingHours[]>(`/api/branches/${branchId}/working-hours`, accessToken)) });
  const hookahs = useQuery({ queryKey: ["hookahs", branchId], enabled: branchScoped && hasPermission(role, "branches.manage"), queryFn: () => authorized((accessToken) => getJson<Hookah[]>(`/api/hookahs?branchId=${branchId}`, accessToken)) });
  const runtimeOrders = useQuery({ queryKey: ["runtime-orders", branchId], enabled: branchScoped && hasPermission(role, "orders.manage"), queryFn: () => authorized((accessToken) => getJson<RuntimeOrder[]>(`/api/orders/runtime/branch/${branchId}`, accessToken)) });
  const bookings = useQuery({ queryKey: ["bookings", branchId], enabled: branchScoped && hasPermission(role, "bookings.manage"), queryFn: () => authorized((accessToken) => getJson<Booking[]>(`/api/bookings?branchId=${branchId}`, accessToken)) });
  const inventory = useQuery({ queryKey: ["inventory", branchId], enabled: branchScoped && hasPermission(role, "inventory.manage"), queryFn: () => authorized((accessToken) => getJson<InventoryItem[]>(`/api/inventory?branchId=${branchId}&lowStockOnly=false`, accessToken)) });
  const inventoryMovements = useQuery({ queryKey: ["inventory-movements", branchId], enabled: branchScoped && hasPermission(role, "inventory.manage"), queryFn: () => authorized((accessToken) => getJson<InventoryMovement[]>(`/api/inventory/movements?branchId=${branchId}`, accessToken)) });
  const mixes = useQuery({ queryKey: ["mixes"], enabled: canUseApp && hasPermission(role, "mixes.manage"), queryFn: () => authorized((accessToken) => getJson<Mix[]>("/api/mixes?publicOnly=false", accessToken)) });
  const bowls = useQuery({ queryKey: ["bowls"], enabled: canUseApp && hasPermission(role, "mixes.manage"), queryFn: () => authorized((accessToken) => getJson<Bowl[]>("/api/bowls", accessToken)) });
  const tobaccos = useQuery({ queryKey: ["tobaccos"], enabled: canUseApp && hasPermission(role, "mixes.manage"), queryFn: () => authorized((accessToken) => getJson<Tobacco[]>("/api/tobaccos?isActive=true", accessToken)) });
  const metrics = useQuery({ queryKey: ["metrics", branchId, analyticsFromDate, analyticsToDate], enabled: branchScoped && analyticsRangeValid && hasPermission(role, "analytics.read"), queryFn: () => authorized((accessToken) => getJson<DashboardMetrics>(`/api/analytics/dashboard?${analyticsQuery}`, accessToken)) });
  const topMixes = useQuery({ queryKey: ["top-mixes", branchId, analyticsFromDate, analyticsToDate], enabled: branchScoped && analyticsRangeValid && hasPermission(role, "analytics.read"), queryFn: () => authorized((accessToken) => getJson<TopMixMetric[]>(`/api/analytics/top-mixes?${analyticsQuery}`, accessToken)) });
  const tobaccoUsage = useQuery({ queryKey: ["tobacco-usage", branchId], enabled: branchScoped && hasPermission(role, "analytics.read"), queryFn: () => authorized((accessToken) => getJson<TobaccoUsageMetric[]>(`/api/analytics/tobacco-usage?branchId=${branchId}`, accessToken)) });
  const staffPerformance = useQuery({ queryKey: ["staff-performance", branchId, analyticsFromDate, analyticsToDate], enabled: branchScoped && analyticsRangeValid && hasPermission(role, "analytics.read"), queryFn: () => authorized((accessToken) => getJson<StaffPerformanceMetric[]>(`/api/analytics/staff-performance?${analyticsQuery}`, accessToken)) });
  const tableLoad = useQuery({ queryKey: ["table-load", branchId, analyticsFromDate, analyticsToDate], enabled: branchScoped && analyticsRangeValid && hasPermission(role, "analytics.read"), queryFn: () => authorized((accessToken) => getJson<TableLoadMetric[]>(`/api/analytics/table-load?${analyticsQuery}`, accessToken)) });
  const notifications = useQuery({ queryKey: ["notifications"], enabled: canUseApp && hasPermission(role, "bookings.manage"), queryFn: () => authorized((accessToken) => getJson<NotificationItem[]>("/api/notifications?unreadOnly=false", accessToken)) });
  const notificationTemplates = useQuery({ queryKey: ["notification-templates"], enabled: canUseApp && hasPermission(role, "bookings.manage"), queryFn: () => authorized((accessToken) => getJson<NotificationTemplate[]>("/api/notifications/templates", accessToken)) });
  const payments = useQuery({ queryKey: ["payments"], enabled: canUseApp && hasPermission(role, "orders.manage"), queryFn: () => authorized((accessToken) => getJson<Payment[]>("/api/payments", accessToken)) });
  const reviews = useQuery({ queryKey: ["reviews"], enabled: canUseApp && hasPermission(role, "orders.manage"), queryFn: () => authorized((accessToken) => getJson<Review[]>("/api/reviews", accessToken)) });
  const promocodes = useQuery({ queryKey: ["promocodes"], enabled: canUseApp && hasPermission(role, "orders.manage"), queryFn: () => authorized((accessToken) => getJson<Promocode[]>("/api/promocodes?activeOnly=false", accessToken)) });
  const staffUsers = useQuery({ queryKey: ["staff-users", branchId], enabled: branchScoped && hasPermission(role, "staff.manage"), queryFn: () => authorized((accessToken) => getJson<UserProfile[]>(`/api/users?branchId=${branchId}&status=active`, accessToken)) });
  const clients = useQuery({ queryKey: ["clients"], enabled: canUseApp && (hasPermission(role, "bookings.manage") || hasPermission(role, "orders.manage")), queryFn: () => authorized((accessToken) => getJson<UserProfile[]>("/api/users?role=CLIENT&status=active", accessToken)) });
  const staffShifts = useQuery({ queryKey: ["staff-shifts", branchId], enabled: branchScoped && hasPermission(role, "staff.manage"), queryFn: () => authorized((accessToken) => getJson<StaffShift[]>(`/api/staff/shifts?branchId=${branchId}`, accessToken)) });

  const statusMutation = useMutation({
    mutationFn: ({ orderId, status }: { orderId: string; status: string }) => patchJson(`/api/orders/${orderId}/status`, { status }, token),
    onSuccess: async () => { setToast("Статус заказа обновлен"); await Promise.all([queryClient.invalidateQueries({ queryKey: ["runtime-orders"] }), queryClient.invalidateQueries({ queryKey: ["floor-plan"] })]); },
    onError: (error) => setToast(error instanceof Error ? error.message : "Не удалось обновить статус")
  });

  const coalMutation = useMutation({
    mutationFn: (orderId: string) => postJson(`/api/orders/${orderId}/coal-change`, { changedAt: new Date().toISOString() }, token),
    onSuccess: async () => { setToast("Замена углей зафиксирована"); await queryClient.invalidateQueries({ queryKey: ["runtime-orders"] }); },
    onError: (error) => setToast(error instanceof Error ? error.message : "Не удалось отметить угли")
  });

  const tables = floorPlan.data?.tables ?? [];
  const activeOrders = runtimeOrders.data ?? [];
  const clientUsers = clients.data ?? [];
  const notificationUsers = useMemo(() => uniqueProfiles([...(staffUsers.data ?? []), ...clientUsers]), [staffUsers.data, clientUsers]);
  const filteredOrders = useMemo(() => activeOrders.filter((order) => `${order.orderId} ${order.status} ${order.tableId}`.toLowerCase().includes(search.toLowerCase())), [activeOrders, search]);
  const branchName = branches.data?.find((branch) => branch.id === branchId)?.name ?? session.profile?.branchId ?? "филиал не выбран";

  if (!hydrated) return <main className="auth-shell"><div className="auth-card"><LoadingState label="Загружаем сессию..." /></div></main>;
  if (!token || profile.isError) return <LoginScreen onLogin={setAuth} error={profile.error instanceof Error ? profile.error.message : undefined} />;
  if (!role) return <main className="auth-shell"><div className="auth-card"><LoadingState label="Проверяем профиль..." /></div></main>;

  return (
    <main className="shell">
      <aside className="sidebar">
        <div className="brand"><span className="brand-mark"><Flame size={20} /></span><span>Hookah CRM</span></div>
        <div className="role-card"><span><ShieldCheck size={15} /> Сессия</span><strong>{session.profile?.name}</strong><small>{role}</small><button className="ghost" onClick={handleLogout}><LogOut size={15} />Выйти</button></div>
        <nav className="nav" aria-label="CRM sections">{allowedSections.map((item) => <button className={section === item.id ? "active" : ""} onClick={() => setSection(item.id)} key={item.id}>{item.icon}{item.label}</button>)}{hasPermission(role, "tenants.manage") && <a className="admin-link" href="/admin/tenants"><Building2 size={18} />Тенанты</a>}{role === "OWNER" && <a className="admin-link" href="/admin/roles">Роли</a>}{role === "OWNER" && <a className="admin-link" href="/admin/audit">Audit</a>}</nav>
      </aside>

      <section className="main">
        <header className="topbar">
          <div className="title"><h1>Операционный экран</h1><p>{branchName} · строгий backend mode, RBAC и JWT-сессия.</p></div>
          <div className="toolbar"><label className="search"><Search size={16} /><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Поиск" /></label></div>
        </header>

        <QueryError queries={[branches, floorPlan, runtimeOrders, bookings, inventory, mixes, metrics, clients]} />

        <div className="kpi-grid">
          <Kpi label="Выручка" value={hasPermission(role, "analytics.read") ? `${Math.round(metrics.data?.revenue ?? 0).toLocaleString("ru-RU")} ₽` : "нет доступа"} />
          <Kpi label="Заказы" value={String(metrics.data?.ordersCount ?? activeOrders.length)} />
          <Kpi label="Средний чек" value={hasPermission(role, "analytics.read") ? `${Math.round(metrics.data?.averageCheck ?? 0)} ₽` : "нет доступа"} />
          <Kpi label="No-show" value={hasPermission(role, "analytics.read") ? `${metrics.data?.noShowRate ?? 0}%` : "нет доступа"} tone="warn" />
        </div>

        {section === "floor" && <FloorSection branchId={branchId} branches={branches.data ?? []} workingHours={workingHours.data ?? []} floorPlan={floorPlan.data} hookahs={hookahs.data ?? []} tables={tables} orders={filteredOrders} token={token} setBranchId={setBranchId} onChanged={() => Promise.all([queryClient.invalidateQueries({ queryKey: ["branches"] }), queryClient.invalidateQueries({ queryKey: ["floor-plan"] }), queryClient.invalidateQueries({ queryKey: ["working-hours"] }), queryClient.invalidateQueries({ queryKey: ["hookahs"] })])} onStatus={(orderId, status) => statusMutation.mutate({ orderId, status })} onCoal={(orderId) => coalMutation.mutate(orderId)} />}
        {section === "orders" && <OrdersSection orders={filteredOrders} bookings={bookings.data ?? []} tables={tables} mixes={mixes.data ?? []} hookahs={hookahs.data ?? []} clients={clientUsers} staff={staffUsers.data ?? []} token={token} branchId={branchId} onChanged={() => queryClient.invalidateQueries({ queryKey: ["runtime-orders"] })} onStatus={(orderId, status) => statusMutation.mutate({ orderId, status })} onCoal={(orderId) => coalMutation.mutate(orderId)} />}
        {section === "bookings" && <BookingsSection bookings={bookings.data ?? []} payments={payments.data ?? []} tables={tables} mixes={mixes.data ?? []} hookahs={hookahs.data ?? []} clients={clientUsers} branchId={branchId} token={token} onChanged={() => Promise.all([queryClient.invalidateQueries({ queryKey: ["bookings"] }), queryClient.invalidateQueries({ queryKey: ["payments"] })])} />}
        {section === "inventory" && <InventorySection inventory={inventory.data ?? []} movements={inventoryMovements.data ?? []} tobaccos={tobaccos.data ?? []} token={token} branchId={branchId} onChanged={() => Promise.all([queryClient.invalidateQueries({ queryKey: ["inventory"] }), queryClient.invalidateQueries({ queryKey: ["inventory-movements"] })])} />}
        {section === "mixology" && <MixologySection mixes={mixes.data ?? []} bowls={bowls.data ?? []} tobaccos={tobaccos.data ?? []} token={token} onChanged={() => queryClient.invalidateQueries({ queryKey: ["mixes"] })} />}
        {section === "staff" && <StaffSection token={token} branchId={branchId} users={staffUsers.data ?? []} shifts={staffShifts.data ?? []} onChanged={() => Promise.all([queryClient.invalidateQueries({ queryKey: ["staff-users"] }), queryClient.invalidateQueries({ queryKey: ["staff-shifts"] })])} />}
        {section === "analytics" && <AnalyticsSection metrics={metrics.data} metricsQuery={metrics} topMixes={topMixes.data ?? []} topMixesQuery={topMixes} tobaccoUsage={tobaccoUsage.data ?? []} tobaccoUsageQuery={tobaccoUsage} staffPerformance={staffPerformance.data ?? []} staffPerformanceQuery={staffPerformance} tableLoad={tableLoad.data ?? []} tableLoadQuery={tableLoad} mixes={mixes.data ?? []} tobaccos={tobaccos.data ?? []} staff={staffUsers.data ?? []} tables={tables} from={analyticsFromDate} to={analyticsToDate} onRange={(from, to) => { setAnalyticsFromDate(from); setAnalyticsToDate(to); }} />}
        {section === "notifications" && <NotificationsSection notifications={notifications.data ?? []} templates={notificationTemplates.data ?? []} users={notificationUsers} token={token} onChanged={() => Promise.all([queryClient.invalidateQueries({ queryKey: ["notifications"] }), queryClient.invalidateQueries({ queryKey: ["notification-templates"] })])} />}
        {section === "reviews" && <ReviewsSection reviews={reviews.data ?? []} mixes={mixes.data ?? []} clients={clientUsers} token={token} onChanged={() => queryClient.invalidateQueries({ queryKey: ["reviews"] })} />}
        {section === "promo" && <PromoSection promocodes={promocodes.data ?? []} token={token} onChanged={() => queryClient.invalidateQueries({ queryKey: ["promocodes"] })} />}

        {toast && <button className="toast" onClick={() => setToast("")}>{toast}</button>}
      </section>
    </main>
  );
}

function LoginScreen({ onLogin, error }: { onLogin: (auth: Awaited<ReturnType<typeof loginStaff>>) => void; error?: string }) {
  const [phone, setPhone] = useState("");
  const [password, setPassword] = useState("");
  const [notice, setNotice] = useState(error ?? "");
  const phoneError = phone && !isValidPhone(phone) ? "Телефон должен быть в формате +79990000000." : "";
  const mutation = useMutation({ mutationFn: () => loginStaff(normalizePhone(phone), password), onSuccess: onLogin, onError: (err) => setNotice(err instanceof Error ? err.message : "Не удалось войти") });
  return <main className="auth-shell"><section className="auth-card"><div className="brand"><span className="brand-mark"><LockKeyhole size={20} /></span><span>Hookah CRM</span></div><h1>Вход персонала</h1><p>Введите телефон и пароль сотрудника. Доступ к разделам ограничивается ролью и permission matrix.</p><label className="field"><span>Телефон</span><input type="tel" inputMode="tel" value={phone} onChange={(event) => setPhone(normalizePhoneInput(event.target.value))} placeholder="+79990000000" /></label><FieldError message={phoneError} /><label className="field"><span>Пароль</span><input type="password" value={password} onChange={(event) => setPassword(event.target.value)} /></label><button className="primary" onClick={() => mutation.mutate()} disabled={mutation.isPending || !isValidPhone(phone) || !password}>{mutation.isPending ? "Входим" : "Войти"}</button>{notice && <FormError message={notice} />}</section></main>;
}

function QueryError({ queries }: { queries: Array<{ error: unknown; isError: boolean }> }) {
  const first = queries.find((query) => query.isError)?.error;
  if (!first) return null;
  return <FormError error={first} />;
}

function Kpi({ label, value, tone }: { label: string; value: string; tone?: "warn" }) { return <article className={`kpi ${tone ?? ""}`}><span>{label}</span><strong>{value}</strong></article>; }

function FloorSection({ branchId, branches, workingHours, floorPlan, hookahs, tables, orders, token, setBranchId, onChanged, onStatus, onCoal }: { branchId: string; branches: Branch[]; workingHours: BranchWorkingHours[]; floorPlan?: FloorPlan; hookahs: Hookah[]; tables: Table[]; orders: RuntimeOrder[]; token: string; setBranchId: (branchId: string) => void; onChanged: () => void | Promise<unknown>; onStatus: (orderId: string, status: string) => void; onCoal: (orderId: string) => void }) {
  const moveTable = useMutation({
    mutationFn: ({ table, xPosition, yPosition }: { table: Table; xPosition: number; yPosition: number }) => patchJson(`/api/tables/${table.id}`, { xPosition, yPosition }, token),
    onSuccess: onChanged
  });
  const moveZone = useMutation({
    mutationFn: ({ zone, xPosition, yPosition }: { zone: FloorPlan["zones"][number]; xPosition: number; yPosition: number }) => patchJson(`/api/zones/${zone.id}`, { xPosition, yPosition }, token),
    onSuccess: onChanged
  });
  const handleDrop = (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    const zone = floorPlan?.zones.find((item) => item.id === event.dataTransfer.getData("zone/id"));
    if (zone) {
      const rect = event.currentTarget.getBoundingClientRect();
      const xPosition = Math.round(Math.max(0, Math.min(760, ((event.clientX - rect.left) / rect.width) * 800)));
      const yPosition = Math.round(Math.max(0, Math.min(520, ((event.clientY - rect.top) / rect.height) * 600)));
      moveZone.mutate({ zone, xPosition, yPosition });
      return;
    }
    const table = tables.find((item) => item.id === event.dataTransfer.getData("table/id"));
    if (!table) return;
    const rect = event.currentTarget.getBoundingClientRect();
    const leftPercent = Math.max(0, Math.min(78, ((event.clientX - rect.left) / rect.width) * 100));
    const topPercent = Math.max(0, Math.min(78, ((event.clientY - rect.top) / rect.height) * 100));
    moveTable.mutate({ table, xPosition: Math.round(leftPercent * 7), yPosition: Math.round(topPercent * 6) });
  };
  return <div className="dashboard"><section className="section wide"><div className="section-head"><h2>Схема зала</h2><span className="meta">drag/drop zones + tables, branch, hours, halls, hookahs CRUD</span></div><BranchCrud branchId={branchId} branches={branches} token={token} setBranchId={setBranchId} onChanged={onChanged} /><WorkingHoursEditor branchId={branchId} workingHours={workingHours} token={token} onChanged={onChanged} /><FloorResourceCrud branchId={branchId} floorPlan={floorPlan} hookahs={hookahs} token={token} onChanged={onChanged} /><div className="floor" onDragOver={(event) => event.preventDefault()} onDrop={handleDrop}>{floorPlan?.zones.map((zone) => <div draggable className="zone-box" onDragStart={(event) => event.dataTransfer.setData("zone/id", zone.id)} style={{ left: `${Math.min(92, Number(zone.xPosition) / 8)}%`, top: `${Math.min(86, Number(zone.yPosition) / 6)}%`, width: `${Math.max(12, Math.min(96, Number(zone.width) / 8))}%`, height: `${Math.max(12, Math.min(88, Number(zone.height) / 6))}%`, borderColor: zone.color ?? "#1e765f", backgroundColor: zoneColor(zone.color) }} key={zone.id}><strong>{zone.name}</strong></div>)}{tables.length === 0 ? <EmptyState label="Столы не созданы" /> : tables.map((table) => { const order = orders.find((candidate) => candidate.tableId === table.id); return <button draggable className={`table-dot ${table.status !== "FREE" || order ? "busy" : ""}`} onDragStart={(event) => event.dataTransfer.setData("table/id", table.id)} style={{ left: `${Math.min(78, Number(table.xPosition) / 7)}%`, top: `${Math.min(78, Number(table.yPosition) / 6)}%` }} key={table.id}><strong>{table.name}</strong><span>{order?.status ?? table.status}</span></button>; })}</div></section><section className="section"><OrderQueue orders={orders} onStatus={onStatus} onCoal={onCoal} /></section></div>;
}

function BranchCrud({ branchId, branches, token, setBranchId, onChanged }: { branchId: string; branches: Branch[]; token: string; setBranchId: (branchId: string) => void; onChanged: () => void | Promise<unknown> }) {
  const current = branches.find((branch) => branch.id === branchId);
  const [name, setName] = useState(current?.name ?? "");
  const [address, setAddress] = useState(current?.address ?? "");
  const [phone, setPhone] = useState(current?.phone ?? "");
  const [timezone, setTimezone] = useState(current?.timezone ?? "Europe/Moscow");
  useEffect(() => { if (current) { setName(current.name); setAddress(current.address); setPhone(current.phone); setTimezone(current.timezone); } }, [current]);
  const branchValidation = !name.trim() ? "Введите название филиала." : !address.trim() ? "Введите адрес." : !isValidPhone(phone) ? "Телефон должен быть в формате +79990000000." : !timezone.trim() ? "Введите timezone." : "";
  const create = useMutation({ mutationFn: () => postJson<Branch>("/api/branches", { name: name.trim(), address: address.trim(), phone: normalizePhone(phone), timezone: timezone.trim() }, token), onSuccess: async (branch) => { setBranchId(branch.id); await onChanged(); } });
  const update = useMutation({ mutationFn: () => patchJson(`/api/branches/${branchId}`, { name: name.trim(), address: address.trim(), phone: normalizePhone(phone), timezone: timezone.trim(), isActive: true }, token), onSuccess: onChanged });
  const deactivate = useMutation({ mutationFn: () => patchJson(`/api/branches/${branchId}`, { isActive: false }, token), onSuccess: onChanged });
  return <div className="crud-card"><h3>Филиалы</h3><CrudToolbar><FormField label="Текущий филиал"><select value={branchId} onChange={(e) => setBranchId(e.target.value)}>{branches.map((branch) => <option value={branch.id} key={branch.id}>{branch.name}{branch.isActive ? "" : " (off)"}</option>)}</select></FormField><FormField label="Название"><input value={name} onChange={(e) => setName(e.target.value)} placeholder="Hookah Place Центр" /></FormField><FormField label="Адрес"><input value={address} onChange={(e) => setAddress(e.target.value)} placeholder="ул. Ленина, 1" /></FormField><FormField label="Телефон"><input type="tel" inputMode="tel" value={phone} onChange={(e) => setPhone(normalizePhoneInput(e.target.value))} placeholder="+79990000000" /></FormField><FormField label="Timezone"><input value={timezone} onChange={(e) => setTimezone(e.target.value)} placeholder="Europe/Moscow" /></FormField><CrudRowActions><ActionButton disabled={Boolean(branchValidation)} pending={create.isPending} pendingLabel="Создаем..." onClick={() => create.mutate()}>Создать</ActionButton><ActionButton disabled={Boolean(branchValidation) || !branchId} pending={update.isPending} pendingLabel="Сохраняем..." onClick={() => update.mutate()}>Сохранить</ActionButton><ActionButton danger disabled={!branchId} pending={deactivate.isPending} pendingLabel="Отключаем..." confirm="Отключить запись?" onClick={() => deactivate.mutate()}>Отключить</ActionButton></CrudRowActions></CrudToolbar>{validationText(branchValidation)}{mutationStatus(create, "Филиал создан")}{mutationStatus(update, "Филиал обновлен")}{mutationStatus(deactivate, "Филиал отключен")}</div>;
}

function WorkingHoursEditor({ branchId, workingHours, token, onChanged }: { branchId: string; workingHours: BranchWorkingHours[]; token: string; onChanged: () => void | Promise<unknown> }) {
  const dayNames = ["Вс", "Пн", "Вт", "Ср", "Чт", "Пт", "Сб"];
  const defaultRows = dayNames.map((_, dayOfWeek) => ({ branchId, dayOfWeek, opensAt: "12:00:00", closesAt: "02:00:00", isClosed: false }));
  const [rows, setRows] = useState<BranchWorkingHours[]>(defaultRows);
  useEffect(() => {
    const byDay = new Map(workingHours.map((item) => [item.dayOfWeek, item]));
    setRows(defaultRows.map((row) => byDay.get(row.dayOfWeek) ?? row));
  }, [branchId, workingHours]);
  const save = useMutation({
    mutationFn: () => putJson(`/api/branches/${branchId}/working-hours`, rows.map((row) => ({ dayOfWeek: row.dayOfWeek, opensAt: normalizeTime(row.opensAt), closesAt: normalizeTime(row.closesAt), isClosed: row.isClosed })), token),
    onSuccess: onChanged
  });
  const updateRow = (dayOfWeek: number, patch: Partial<BranchWorkingHours>) => setRows((current) => current.map((row) => row.dayOfWeek === dayOfWeek ? { ...row, ...patch } : row));
  return <div className="crud-card"><h3>График работы</h3><div className="hours-grid">{rows.map((row) => <div className="hours-row" key={row.dayOfWeek}><strong>{dayNames[row.dayOfWeek]}</strong><input type="time" value={row.opensAt.slice(0, 5)} onChange={(e) => updateRow(row.dayOfWeek, { opensAt: e.target.value })} /><input type="time" value={row.closesAt.slice(0, 5)} onChange={(e) => updateRow(row.dayOfWeek, { closesAt: e.target.value })} /><label className="check"><input type="checkbox" checked={row.isClosed} onChange={(e) => updateRow(row.dayOfWeek, { isClosed: e.target.checked })} />закрыто</label></div>)}</div><button className="mini-button" disabled={!branchId || save.isPending} onClick={() => save.mutate()}>Сохранить график</button></div>;
}

function normalizeTime(value: string) {
  return value.length === 5 ? `${value}:00` : value;
}

function FloorResourceCrud({ branchId, floorPlan, hookahs, token, onChanged }: { branchId: string; floorPlan?: FloorPlan; hookahs: Hookah[]; token: string; onChanged: () => void | Promise<unknown> }) {
  const halls = floorPlan?.halls ?? [];
  const zones = floorPlan?.zones ?? [];
  const tables = floorPlan?.tables ?? [];
  const [hallName, setHallName] = useState("");
  const [hallDescription, setHallDescription] = useState("");
  const [zoneName, setZoneName] = useState("");
  const [zoneDescription, setZoneDescription] = useState("");
  const [zoneColorValue, setZoneColorValue] = useState("#1e765f");
  const [zoneX, setZoneX] = useState(40);
  const [zoneY, setZoneY] = useState(40);
  const [zoneWidth, setZoneWidth] = useState(360);
  const [zoneHeight, setZoneHeight] = useState(220);
  const [tableName, setTableName] = useState("");
  const [tableCapacity, setTableCapacity] = useState(4);
  const [tableZoneId, setTableZoneId] = useState("");
  const [tableX, setTableX] = useState(120);
  const [tableY, setTableY] = useState(220);
  const [hookahName, setHookahName] = useState("");
  const [hookahBrand, setHookahBrand] = useState("");
  const [hookahModel, setHookahModel] = useState("");
  const [hookahPhotoUrl, setHookahPhotoUrl] = useState("");
  const [hallId, setHallId] = useState("");
  const selectedHallId = hallId;
  const createHall = useMutation({ mutationFn: () => postJson("/api/halls", { branchId, name: hallName.trim(), description: hallDescription.trim() || null }, token), onSuccess: async () => { setHallName(""); setHallDescription(""); await onChanged(); } });
  const createZone = useMutation({ mutationFn: () => postJson("/api/zones", { branchId, name: zoneName.trim(), description: zoneDescription.trim() || null, color: zoneColorValue, xPosition: zoneX, yPosition: zoneY, width: zoneWidth, height: zoneHeight }, token), onSuccess: async () => { setZoneName(""); setZoneDescription(""); setZoneColorValue("#1e765f"); setZoneX(40); setZoneY(40); setZoneWidth(360); setZoneHeight(220); await onChanged(); } });
  const createTable = useMutation({ mutationFn: () => postJson("/api/tables", { hallId: selectedHallId, zoneId: tableZoneId || null, name: tableName.trim(), capacity: tableCapacity, xPosition: tableX, yPosition: tableY }, token), onSuccess: async () => { setTableName(""); setTableCapacity(4); await onChanged(); } });
  const createHookah = useMutation({ mutationFn: () => postJson("/api/hookahs", { branchId, name: hookahName.trim(), brand: hookahBrand.trim(), model: hookahModel.trim(), status: "AVAILABLE", photoUrl: hookahPhotoUrl.trim() || null }, token), onSuccess: async () => { setHookahName(""); setHookahBrand(""); setHookahModel(""); setHookahPhotoUrl(""); await onChanged(); } });
  return (
    <div className="crud-grid">
      <div className="crud-card">
        <h3>Залы</h3>
        <CrudToolbar title="Новый зал">
          <FormField label="Название">
            <input value={hallName} onChange={(e) => setHallName(e.target.value)} placeholder="Основной зал" />
          </FormField>
          <FormField label="Описание">
            <input value={hallDescription} onChange={(e) => setHallDescription(e.target.value)} placeholder="Первый этаж" />
          </FormField>
          <button disabled={!branchId || !hallName.trim() || createHall.isPending} onClick={() => createHall.mutate()}>Создать</button>
        </CrudToolbar>
        {mutationStatus(createHall, "Зал создан")}
        {halls.length === 0 ? <EmptyState label="Залов нет" /> : halls.map((hall) => <HallRow key={hall.id} hall={hall} token={token} onChanged={onChanged} />)}
      </div>
      <div className="crud-card">
        <h3>Зоны</h3>
        <CrudToolbar title="Новая зона">
          <FormField label="Название">
            <input value={zoneName} onChange={(e) => setZoneName(e.target.value)} placeholder="VIP" />
          </FormField>
          <FormField label="Описание">
            <input value={zoneDescription} onChange={(e) => setZoneDescription(e.target.value)} placeholder="Описание" />
          </FormField>
          <FormField label="Цвет">
            <input type="color" value={zoneColorValue} onChange={(e) => setZoneColorValue(e.target.value)} />
          </FormField>
          <FormField label="X">
            <input type="number" min={0} value={zoneX} onChange={(e) => setZoneX(Number(e.target.value))} placeholder="X" />
          </FormField>
          <FormField label="Y">
            <input type="number" min={0} value={zoneY} onChange={(e) => setZoneY(Number(e.target.value))} placeholder="Y" />
          </FormField>
          <FormField label="Ширина">
            <input type="number" min={1} value={zoneWidth} onChange={(e) => setZoneWidth(Number(e.target.value))} placeholder="W" />
          </FormField>
          <FormField label="Высота">
            <input type="number" min={1} value={zoneHeight} onChange={(e) => setZoneHeight(Number(e.target.value))} placeholder="H" />
          </FormField>
          <button disabled={!branchId || !zoneName.trim() || zoneWidth <= 0 || zoneHeight <= 0 || createZone.isPending} onClick={() => createZone.mutate()}>Создать</button>
        </CrudToolbar>
        {mutationStatus(createZone, "Зона создана")}
        {zones.length === 0 ? <EmptyState label="Зон нет" /> : zones.map((zone) => <ZoneRow key={zone.id} zone={zone} token={token} onChanged={onChanged} />)}
      </div>
      <div className="crud-card">
        <h3>Столы</h3>
        <CrudToolbar title="Новый стол">
          <FormField label="Зал">
            <select value={selectedHallId} onChange={(e) => setHallId(e.target.value)}><option value="">Выберите зал</option>{halls.map((hall) => <option value={hall.id} key={hall.id}>{hall.name}</option>)}</select>
          </FormField>
          <FormField label="Зона">
            <select value={tableZoneId} onChange={(e) => setTableZoneId(e.target.value)}><option value="">Без зоны</option>{zones.map((zone) => <option value={zone.id} key={zone.id}>{zone.name}</option>)}</select>
          </FormField>
          <FormField label="Название">
            <input value={tableName} onChange={(e) => setTableName(e.target.value)} placeholder="Стол 1" />
          </FormField>
          <FormField label="Мест">
            <input type="number" min={1} value={tableCapacity} onChange={(e) => setTableCapacity(Number(e.target.value))} placeholder="4" />
          </FormField>
          <FormField label="X">
            <input type="number" min={0} value={tableX} onChange={(e) => setTableX(Number(e.target.value))} placeholder="X" />
          </FormField>
          <FormField label="Y">
            <input type="number" min={0} value={tableY} onChange={(e) => setTableY(Number(e.target.value))} placeholder="Y" />
          </FormField>
          <button disabled={!selectedHallId || !tableName.trim() || tableCapacity <= 0 || tableX < 0 || tableY < 0 || createTable.isPending} onClick={() => createTable.mutate()}>Создать</button>
        </CrudToolbar>
        {mutationStatus(createTable, "Стол создан")}
        {tables.length === 0 ? <EmptyState label="Столов нет" /> : tables.map((table) => <TableRow key={table.id} table={table} halls={halls} zones={zones} token={token} onChanged={onChanged} />)}
      </div>
      <div className="crud-card">
        <h3>Кальяны</h3>
        <CrudToolbar title="Новый кальян">
          <FormField label="Название">
            <input value={hookahName} onChange={(e) => setHookahName(e.target.value)} placeholder="Alpha X" />
          </FormField>
          <FormField label="Бренд">
            <input value={hookahBrand} onChange={(e) => setHookahBrand(e.target.value)} placeholder="Alpha Hookah" />
          </FormField>
          <FormField label="Модель">
            <input value={hookahModel} onChange={(e) => setHookahModel(e.target.value)} placeholder="X" />
          </FormField>
          <FormField label="Фото URL">
            <input value={hookahPhotoUrl} onChange={(e) => setHookahPhotoUrl(e.target.value)} placeholder="https://..." />
          </FormField>
          <button disabled={!branchId || !hookahName.trim() || !hookahBrand.trim() || !hookahModel.trim() || createHookah.isPending} onClick={() => createHookah.mutate()}>Создать</button>
        </CrudToolbar>
        {mutationStatus(createHookah, "Кальян создан")}
        {hookahs.length === 0 ? <EmptyState label="Кальянов нет" /> : hookahs.map((hookah) => <HookahRow key={hookah.id} hookah={hookah} token={token} onChanged={onChanged} />)}
      </div>
    </div>
  );
}

function HallRow({ hall, token, onChanged }: { hall: FloorPlan["halls"][number]; token: string; onChanged: () => void | Promise<unknown> }) {
  const [name, setName] = useState(hall.name);
  const [description, setDescription] = useState(hall.description ?? "");
  const validation = !name.trim() ? "Название зала обязательно." : "";
  const update = useMutation({ mutationFn: () => patchJson(`/api/halls/${hall.id}`, { name: name.trim(), description: description.trim() || null }, token), onSuccess: onChanged });
  const remove = useMutation({ mutationFn: () => deleteJson(`/api/halls/${hall.id}`, undefined, token), onSuccess: onChanged });
  return <div className="crud-row-block"><div className="crud-row"><FormField label="Название"><input value={name} onChange={(e) => setName(e.target.value)} /></FormField><FormField label="Описание"><input value={description} onChange={(e) => setDescription(e.target.value)} placeholder="Описание" /></FormField><CrudRowActions><ActionButton disabled={Boolean(validation)} pending={update.isPending} pendingLabel="Сохраняем..." onClick={() => update.mutate()}>save</ActionButton><ActionButton danger pending={remove.isPending} pendingLabel="Удаляем..." confirm="Удалить зал?" onClick={() => remove.mutate()}>delete</ActionButton></CrudRowActions></div>{validationText(validation)}{mutationStatus(update, "Зал обновлен")}{mutationStatus(remove, "Зал удален")}</div>;
}

function ZoneRow({ zone, token, onChanged }: { zone: FloorPlan["zones"][number]; token: string; onChanged: () => void | Promise<unknown> }) {
  const [name, setName] = useState(zone.name);
  const [description, setDescription] = useState(zone.description ?? "");
  const [color, setColor] = useState(zone.color ?? "#1e765f");
  const [xPosition, setXPosition] = useState(zone.xPosition);
  const [yPosition, setYPosition] = useState(zone.yPosition);
  const [width, setWidth] = useState(zone.width);
  const [height, setHeight] = useState(zone.height);
  const validation = !name.trim() ? "Название зоны обязательно." : xPosition < 0 || yPosition < 0 ? "Координаты не могут быть отрицательными." : width <= 0 || height <= 0 ? "Размер зоны должен быть больше 0." : "";
  const update = useMutation({ mutationFn: () => patchJson(`/api/zones/${zone.id}`, { name: name.trim(), description: description.trim() || null, color, xPosition, yPosition, width, height, isActive: true }, token), onSuccess: onChanged });
  const remove = useMutation({ mutationFn: () => deleteJson(`/api/zones/${zone.id}`, undefined, token), onSuccess: onChanged });
  return <div className="crud-row-block"><div className="crud-row wide-row"><FormField label="Название"><input value={name} onChange={(e) => setName(e.target.value)} /></FormField><FormField label="Описание"><input value={description} onChange={(e) => setDescription(e.target.value)} placeholder="Описание" /></FormField><FormField label="Цвет"><input type="color" value={color} onChange={(e) => setColor(e.target.value)} /></FormField><FormField label="X"><input type="number" min={0} value={xPosition} onChange={(e) => setXPosition(Number(e.target.value))} /></FormField><FormField label="Y"><input type="number" min={0} value={yPosition} onChange={(e) => setYPosition(Number(e.target.value))} /></FormField><FormField label="Ширина"><input type="number" min={1} value={width} onChange={(e) => setWidth(Number(e.target.value))} /></FormField><FormField label="Высота"><input type="number" min={1} value={height} onChange={(e) => setHeight(Number(e.target.value))} /></FormField><CrudRowActions><ActionButton disabled={Boolean(validation)} pending={update.isPending} pendingLabel="Сохраняем..." onClick={() => update.mutate()}>save</ActionButton><ActionButton danger pending={remove.isPending} pendingLabel="Удаляем..." confirm="Удалить зону?" onClick={() => remove.mutate()}>delete</ActionButton></CrudRowActions></div>{validationText(validation)}{mutationStatus(update, "Зона обновлена")}{mutationStatus(remove, "Зона удалена")}</div>;
}

function TableRow({ table, halls, zones, token, onChanged }: { table: Table; halls: FloorPlan["halls"]; zones: FloorPlan["zones"]; token: string; onChanged: () => void | Promise<unknown> }) {
  const [name, setName] = useState(table.name);
  const [capacity, setCapacity] = useState(table.capacity);
  const [hallId, setHallId] = useState(table.hallId);
  const [zoneId, setZoneId] = useState(table.zoneId ?? "");
  const [status, setStatus] = useState(table.status);
  const [xPosition, setXPosition] = useState(table.xPosition);
  const [yPosition, setYPosition] = useState(table.yPosition);
  const validation = !name.trim() ? "Название стола обязательно." : !hallId ? "Выберите зал." : capacity <= 0 ? "Вместимость должна быть больше 0." : xPosition < 0 || yPosition < 0 ? "Координаты не могут быть отрицательными." : "";
  const update = useMutation({ mutationFn: () => patchJson(`/api/tables/${table.id}`, { name: name.trim(), capacity, hallId, zoneId: zoneId || null, status, xPosition, yPosition, isActive: true }, token), onSuccess: onChanged });
  const remove = useMutation({ mutationFn: () => deleteJson(`/api/tables/${table.id}`, undefined, token), onSuccess: onChanged });
  return <div className="crud-row-block"><div className="crud-row wide-row"><FormField label="Название"><input value={name} onChange={(e) => setName(e.target.value)} /></FormField><FormField label="Зал"><select value={hallId} onChange={(e) => setHallId(e.target.value)}>{halls.map((hall) => <option value={hall.id} key={hall.id}>{hall.name}</option>)}</select></FormField><FormField label="Зона"><select value={zoneId} onChange={(e) => setZoneId(e.target.value)}><option value="">Без зоны</option>{zones.map((zone) => <option value={zone.id} key={zone.id}>{zone.name}</option>)}</select></FormField><FormField label="Мест"><input type="number" min={1} value={capacity} onChange={(e) => setCapacity(Number(e.target.value))} /></FormField><FormField label="Статус"><select value={status} onChange={(e) => setStatus(e.target.value)}>{["FREE","OCCUPIED","RESERVED","CLEANING","OUT_OF_SERVICE"].map((value) => <option key={value}>{value}</option>)}</select></FormField><FormField label="X"><input type="number" min={0} value={xPosition} onChange={(e) => setXPosition(Number(e.target.value))} /></FormField><FormField label="Y"><input type="number" min={0} value={yPosition} onChange={(e) => setYPosition(Number(e.target.value))} /></FormField><CrudRowActions><ActionButton disabled={Boolean(validation)} pending={update.isPending} pendingLabel="Сохраняем..." onClick={() => update.mutate()}>save</ActionButton><ActionButton danger pending={remove.isPending} pendingLabel="Удаляем..." confirm="Удалить стол?" onClick={() => remove.mutate()}>delete</ActionButton></CrudRowActions></div>{validationText(validation)}{mutationStatus(update, "Стол обновлен")}{mutationStatus(remove, "Стол удален")}</div>;
}

function HookahRow({ hookah, token, onChanged }: { hookah: Hookah; token: string; onChanged: () => void | Promise<unknown> }) {
  const [name, setName] = useState(hookah.name);
  const [brand, setBrand] = useState(hookah.brand);
  const [model, setModel] = useState(hookah.model);
  const [status, setStatus] = useState(hookah.status);
  const [photoUrl, setPhotoUrl] = useState(hookah.photoUrl ?? "");
  const [lastServiceAt, setLastServiceAt] = useState(toDateTimeInput(hookah.lastServiceAt));
  const validation = !name.trim() ? "Название кальяна обязательно." : !brand.trim() ? "Бренд обязателен." : !model.trim() ? "Модель обязательна." : "";
  const update = useMutation({ mutationFn: () => patchJson(`/api/hookahs/${hookah.id}`, { name: name.trim(), brand: brand.trim(), model: model.trim(), status, photoUrl: photoUrl.trim() || null, lastServiceAt: lastServiceAt ? new Date(lastServiceAt).toISOString() : null }, token), onSuccess: onChanged });
  const remove = useMutation({ mutationFn: () => deleteJson(`/api/hookahs/${hookah.id}`, undefined, token), onSuccess: onChanged });
  return <div className="crud-row-block"><div className="crud-row wide-row"><FormField label="Название"><input value={name} onChange={(e) => setName(e.target.value)} placeholder="Название" /></FormField><FormField label="Бренд"><input value={brand} onChange={(e) => setBrand(e.target.value)} placeholder="Бренд" /></FormField><FormField label="Модель"><input value={model} onChange={(e) => setModel(e.target.value)} placeholder="Модель" /></FormField><FormField label="Статус"><select value={status} onChange={(e) => setStatus(e.target.value)}>{["AVAILABLE","IN_USE","WASHING","BROKEN","WRITTEN_OFF"].map((value) => <option key={value}>{value}</option>)}</select></FormField><FormField label="Фото URL"><input value={photoUrl} onChange={(e) => setPhotoUrl(e.target.value)} placeholder="Фото URL" /></FormField><FormField label="Сервис"><input type="datetime-local" value={lastServiceAt} onChange={(e) => setLastServiceAt(e.target.value)} /></FormField><CrudRowActions><ActionButton disabled={Boolean(validation)} pending={update.isPending} pendingLabel="Сохраняем..." onClick={() => update.mutate()}>save</ActionButton><ActionButton danger pending={remove.isPending} pendingLabel="Списываем..." confirm="Списать кальян?" onClick={() => remove.mutate()}>delete</ActionButton></CrudRowActions></div>{validationText(validation)}{mutationStatus(update, "Кальян обновлен")}{mutationStatus(remove, "Кальян списан")}</div>;
}

function OrdersSection(props: { orders: RuntimeOrder[]; bookings: Booking[]; tables: Table[]; mixes: Mix[]; hookahs: Hookah[]; clients: UserProfile[]; staff: UserProfile[]; token: string; branchId: string; onChanged: () => void; onStatus: (orderId: string, status: string) => void; onCoal: (orderId: string) => void }) {
  return <section className="section"><CreateOrderForm {...props} /><OrderQueue {...props} token={props.token} staff={props.staff} onChanged={props.onChanged} /></section>;
}

function CreateOrderForm({ tables, mixes, hookahs, bookings, clients, staff, token, branchId, onChanged }: { tables: Table[]; mixes: Mix[]; hookahs: Hookah[]; bookings: Booking[]; clients: UserProfile[]; staff: UserProfile[]; token: string; branchId: string; onChanged: () => void }) {
  const [tableId, setTableId] = useState("");
  const [mixId, setMixId] = useState("");
  const [hookahId, setHookahId] = useState("");
  const [clientId, setClientId] = useState("");
  const [waiterId, setWaiterId] = useState("");
  const [bookingId, setBookingId] = useState("");
  const [price, setPrice] = useState(0);
  const [comment, setComment] = useState("");
  const selectedMix = mixes.find((mix) => mix.id === mixId);
  const waiterOptions = staff.filter((user) => user.role === "WAITER" || user.role === "MANAGER");
  const canSubmit = Boolean(branchId && tableId && hookahId && selectedMix?.bowlId && mixId && price >= 0);
  const mutation = useMutation({
    mutationFn: () => {
      if (!canSubmit) throw new Error("Выберите стол, кальян и микс.");
      return postJson("/api/orders", { branchId, tableId, clientId: clientId || null, hookahId, bowlId: selectedMix!.bowlId, mixId, bookingId: bookingId || null, waiterId: waiterId || null, price: price > 0 ? price : null, comment: comment || null }, token);
    },
    onSuccess: () => { setTableId(""); setMixId(""); setHookahId(""); setClientId(""); setWaiterId(""); setBookingId(""); setPrice(0); setComment(""); onChanged(); }
  });
  return <><CrudToolbar title="Новый заказ"><FormField label="Клиент"><UserSelect users={clients} value={clientId} onChange={setClientId} placeholder="Клиент не указан" /></FormField><FormField label="Стол"><select value={tableId} onChange={(e) => setTableId(e.target.value)}><option value="">Выберите стол</option>{tables.map((table) => <option value={table.id} key={table.id}>{table.name}</option>)}</select></FormField><FormField label="Микс"><select value={mixId} onChange={(e) => setMixId(e.target.value)}><option value="">Выберите микс</option>{mixes.map((mix) => <option value={mix.id} key={mix.id}>{mix.name}</option>)}</select></FormField><FormField label="Кальян"><select value={hookahId} onChange={(e) => setHookahId(e.target.value)}><option value="">Выберите кальян</option>{hookahs.map((hookah) => <option value={hookah.id} key={hookah.id}>{hookah.name}</option>)}</select></FormField><FormField label="Официант"><UserSelect users={waiterOptions} value={waiterId} onChange={setWaiterId} placeholder="Официант не назначен" /></FormField><FormField label="Бронь"><select value={bookingId} onChange={(e) => setBookingId(e.target.value)}><option value="">Без брони</option>{bookings.map((booking) => <option value={booking.id} key={booking.id}>{shortId(booking.id)} · {timeLabel(booking.startTime)} · {booking.status}</option>)}</select></FormField><FormField label="Цена override"><input type="number" min={0} value={price} onChange={(e) => setPrice(Number(e.target.value))} placeholder="0" /></FormField><FormField label="Комментарий"><input value={comment} onChange={(e) => setComment(e.target.value)} placeholder="Средняя крепость, без холода" /></FormField><button onClick={() => mutation.mutate()} disabled={!canSubmit || mutation.isPending}><Plus size={15} />Создать</button></CrudToolbar>{clients.length === 0 && <EmptyState label="Клиенты не найдены" description="Создайте клиентский профиль в клиентском приложении или через API." />}{validationText(price < 0 ? "Цена не может быть отрицательной." : "")}{mutationStatus(mutation, "Заказ создан")}</>;
}

function OrderQueue({ orders, onStatus, onCoal, token, staff = [], onChanged }: { orders: RuntimeOrder[]; onStatus: (orderId: string, status: string) => void; onCoal: (orderId: string) => void; token?: string; staff?: UserProfile[]; onChanged?: () => void }) {
  const [masterId, setMasterId] = useState("");
  const [cancelReason, setCancelReason] = useState("");
  const assign = useMutation({ mutationFn: (orderId: string) => patchJson(`/api/orders/${orderId}/assign-hookah-master`, { hookahMasterId: masterId }, token), onSuccess: onChanged });
  const cancel = useMutation({ mutationFn: (orderId: string) => deleteJson(`/api/orders/${orderId}`, { reason: cancelReason }, token), onSuccess: onChanged });
  return <><div className="section-head"><h2>Активные заказы</h2><span className="meta">{orders.length} в работе</span></div><CrudToolbar title="Назначение / отмена">{staff.length > 0 && <FormField label="Мастер"><select value={masterId} onChange={(e) => setMasterId(e.target.value)}><option value="">Выберите мастера</option>{staff.map((user) => <option value={user.id} key={user.id}>{user.name} · {user.role}</option>)}</select></FormField>}<FormField label="Причина отмены"><input value={cancelReason} onChange={(e) => setCancelReason(e.target.value)} placeholder="Клиент отказался" /></FormField></CrudToolbar>{mutationStatus(assign, "Мастер назначен")}{mutationStatus(cancel, "Заказ отменен")}<div className="queue cards">{orders.length === 0 ? <EmptyState label="Активных заказов нет" /> : orders.map((order) => <article className="order-card" key={order.orderId}><div className="order-top"><strong>#{shortId(order.orderId)} · стол {shortId(order.tableId)}</strong><span className={`status ${order.status === "SMOKING" ? "ok" : "warn"}`}>{order.status}</span></div><div className="meta">Кальян {shortId(order.hookahId)} · {order.totalPrice} ₽ · мастер {shortId(order.hookahMasterId)}</div><div className="coal"><TimerReset size={16} />Следующие угли: {timeLabel(order.nextCoalChangeAt)}</div><div className="actions"><button onClick={() => onStatus(order.orderId, "READY")}>Готов</button><button onClick={() => onStatus(order.orderId, "SERVED")}>Вынесен</button><button onClick={() => onCoal(order.orderId)}>Угли</button><button onClick={() => onStatus(order.orderId, "COMPLETED")}>Закрыть</button><ActionButton disabled={!token || !masterId} pending={assign.isPending} pendingLabel="Назначаем..." onClick={() => assign.mutate(order.orderId)}>Назначить</ActionButton><ActionButton danger disabled={!token || !cancelReason.trim()} pending={cancel.isPending} pendingLabel="Отменяем..." confirm="Отменить заказ?" onClick={() => cancel.mutate(order.orderId)}>Отменить</ActionButton></div></article>)}</div></>;
}

function BookingsSection({ bookings, payments, tables, mixes, hookahs, clients, branchId, token, onChanged }: { bookings: Booking[]; payments: Payment[]; tables: Table[]; mixes: Mix[]; hookahs: Hookah[]; clients: UserProfile[]; branchId: string; token: string; onChanged: () => void | Promise<unknown> }) {
  const [clientId, setClientId] = useState("");
  const [tableId, setTableId] = useState("");
  const [mixId, setMixId] = useState("");
  const [hookahId, setHookahId] = useState("");
  const [startTime, setStartTime] = useState(() => defaultDateTimeInput(20));
  const [endTime, setEndTime] = useState(() => defaultDateTimeInput(22));
  const [guestsCount, setGuestsCount] = useState(4);
  const [depositAmount, setDepositAmount] = useState(2000);
  const [comment, setComment] = useState("");
  const [cancelReason, setCancelReason] = useState("");
  const [rescheduleBookingId, setRescheduleBookingId] = useState("");
  const [rescheduleTableId, setRescheduleTableId] = useState("");
  const [rescheduleStartTime, setRescheduleStartTime] = useState(() => defaultDateTimeInput(20));
  const [rescheduleEndTime, setRescheduleEndTime] = useState(() => defaultDateTimeInput(22));
  const selectedMix = mixes.find((mix) => mix.id === mixId);
  const selectedRescheduleBooking = bookings.find((booking) => booking.id === rescheduleBookingId);
  const dateError = !dateRangeIsValid(startTime, endTime) ? "Время окончания должно быть позже начала." : "";
  const rescheduleDateError = !dateRangeIsValid(rescheduleStartTime, rescheduleEndTime) ? "Время окончания переноса должно быть позже начала." : "";
  const canCreateBooking = Boolean(clientId && tableId && selectedMix && guestsCount > 0 && depositAmount >= 0 && dateRangeIsValid(startTime, endTime));
  const canReschedule = Boolean(selectedRescheduleBooking && rescheduleTableId && !rescheduleDateError);
  const paymentsByBooking = useMemo(() => groupPaymentsByBooking(payments), [payments]);
  const create = useMutation({ mutationFn: () => postJson("/api/bookings", { branchId, tableId, clientId, startTime: new Date(startTime).toISOString(), endTime: new Date(endTime).toISOString(), guestsCount, hookahId: hookahId || null, bowlId: selectedMix?.bowlId ?? null, mixId: selectedMix?.id ?? null, comment: comment || null, depositAmount }, token), onSuccess: onChanged });
  const action = useMutation({ mutationFn: ({ id, path }: { id: string; path: string }) => {
    if (path === "cancel" && !cancelReason.trim()) throw new Error("Укажите причину отмены брони.");
    return patchJson(`/api/bookings/${id}/${path}`, path === "cancel" ? { reason: cancelReason } : {}, token);
  }, onSuccess: onChanged });
  const reschedule = useMutation({ mutationFn: () => {
    if (!selectedRescheduleBooking || !canReschedule) throw new Error("Выберите бронь, стол и корректный диапазон времени.");
    return patchJson(`/api/bookings/${selectedRescheduleBooking.id}/reschedule`, { startTime: new Date(rescheduleStartTime).toISOString(), endTime: new Date(rescheduleEndTime).toISOString(), tableId: rescheduleTableId }, token);
  }, onSuccess: onChanged });
  const selectReschedule = (booking: Booking) => {
    setRescheduleBookingId(booking.id);
    setRescheduleTableId(booking.tableId);
    setRescheduleStartTime(toDateTimeInput(booking.startTime));
    setRescheduleEndTime(toDateTimeInput(booking.endTime));
  };
  return <section className="section"><div className="section-head"><h2>Бронирования</h2><span className="meta">create/status/reschedule/cancel/refund</span></div><PaymentsBoard payments={payments} bookings={bookings} /><RefundPanel payments={payments} token={token} onChanged={onChanged} /><CrudToolbar title="Новая бронь"><FormField label="Клиент"><UserSelect users={clients} value={clientId} onChange={setClientId} placeholder="Выберите клиента" required /></FormField><FormField label="Стол"><select value={tableId} onChange={(e) => setTableId(e.target.value)}><option value="">Выберите стол</option>{tables.map((table) => <option value={table.id} key={table.id}>{table.name}</option>)}</select></FormField><FormField label="Микс"><select value={mixId} onChange={(e) => setMixId(e.target.value)}><option value="">Выберите микс</option>{mixes.map((mix) => <option value={mix.id} key={mix.id}>{mix.name}</option>)}</select></FormField><FormField label="Кальян"><select value={hookahId} onChange={(e) => setHookahId(e.target.value)}><option value="">Без кальяна</option>{hookahs.map((hookah) => <option value={hookah.id} key={hookah.id}>{hookah.name}</option>)}</select></FormField><FormField label="Начало"><input type="datetime-local" value={startTime} onChange={(e) => setStartTime(e.target.value)} /></FormField><FormField label="Окончание"><input type="datetime-local" value={endTime} onChange={(e) => setEndTime(e.target.value)} /></FormField><FormField label="Гости"><input type="number" min={1} value={guestsCount} onChange={(e) => setGuestsCount(Number(e.target.value))} /></FormField><FormField label="Депозит"><input type="number" min={0} value={depositAmount} onChange={(e) => setDepositAmount(Number(e.target.value))} /></FormField><FormField label="Комментарий"><input value={comment} onChange={(e) => setComment(e.target.value)} placeholder="День рождения" /></FormField><button disabled={!canCreateBooking || create.isPending} onClick={() => create.mutate()}>Создать бронь</button></CrudToolbar><CrudToolbar title="Перенос брони"><FormField label="Бронь"><select value={rescheduleBookingId} onChange={(e) => { const booking = bookings.find((candidate) => candidate.id === e.target.value); if (booking) selectReschedule(booking); else setRescheduleBookingId(""); }}><option value="">Выберите бронь</option>{bookings.map((booking) => <option value={booking.id} key={booking.id}>{shortId(booking.id)} · {timeLabel(booking.startTime)} · {booking.status}</option>)}</select></FormField><FormField label="Новый стол"><select value={rescheduleTableId} onChange={(e) => setRescheduleTableId(e.target.value)}><option value="">Новый стол</option>{tables.map((table) => <option value={table.id} key={table.id}>{table.name} · {table.capacity}</option>)}</select></FormField><FormField label="Новое начало"><input type="datetime-local" value={rescheduleStartTime} onChange={(e) => setRescheduleStartTime(e.target.value)} /></FormField><FormField label="Новое окончание"><input type="datetime-local" value={rescheduleEndTime} onChange={(e) => setRescheduleEndTime(e.target.value)} /></FormField><button disabled={!canReschedule || reschedule.isPending} onClick={() => reschedule.mutate()}>Перенести</button></CrudToolbar><CrudToolbar title="Отмена брони"><FormField label="Причина"><input value={cancelReason} onChange={(e) => setCancelReason(e.target.value)} placeholder="Клиент отменил" /></FormField></CrudToolbar>{clients.length === 0 && <EmptyState label="Клиенты не найдены" description="Бронь в CRM требует существующий клиентский профиль." />}{validationText(dateError || rescheduleDateError)}{mutationStatus(create, "Бронь создана")}{mutationStatus(action, "Статус брони обновлен")}{mutationStatus(reschedule, "Бронь перенесена")}<div className="queue cards">{bookings.length === 0 ? <EmptyState label="Броней нет" /> : bookings.map((booking) => { const bookingPayments = paymentsByBooking.get(booking.id) ?? []; const paymentState = summarizePayments(bookingPayments); return <article className="booking-row booking-row-rich" key={booking.id}><div><strong>{timeLabel(booking.startTime)} · {clientName(booking.clientId, clients)}</strong><div className="meta">{tables.find((table) => table.id === booking.tableId)?.name ?? shortId(booking.tableId)} · {booking.guestsCount} гостей · депозит {booking.depositAmount} ₽</div><PaymentMini payments={bookingPayments} /></div><div className="booking-status-stack"><span className={`status ${booking.status === "CONFIRMED" ? "ok" : "warn"}`}>{booking.status}</span><span className={`status ${paymentStatusTone(paymentState.status)}`}>{paymentState.status}</span><small>{paymentState.label}</small></div><div className="row-actions"><button onClick={() => action.mutate({ id: booking.id, path: "confirm" })}>confirm</button><button onClick={() => action.mutate({ id: booking.id, path: "client-arrived" })}>arrived</button><button onClick={() => action.mutate({ id: booking.id, path: "complete" })}>complete</button><button onClick={() => selectReschedule(booking)}>reschedule</button><button onClick={() => action.mutate({ id: booking.id, path: "no-show" })}>no-show</button><ActionButton danger disabled={!cancelReason.trim()} pending={action.isPending} pendingLabel="Отменяем..." confirm="Отменить бронь?" onClick={() => action.mutate({ id: booking.id, path: "cancel" })}>cancel</ActionButton></div></article>; })}</div></section>;
}

function RefundPanel({ payments, token, onChanged }: { payments: Payment[]; token: string; onChanged: () => void | Promise<unknown> }) {
  const refundablePayments = payments.filter((payment) => ["SUCCESS", "PARTIALLY_REFUNDED"].includes(payment.status) && payment.payableAmount > payment.refundedAmount);
  const [paymentId, setPaymentId] = useState("");
  const selectedPayment = refundablePayments.find((payment) => payment.id === paymentId);
  const refundable = selectedPayment ? selectedPayment.payableAmount - selectedPayment.refundedAmount : 0;
  const [amount, setAmount] = useState(1000);
  const [reason, setReason] = useState("");
  useEffect(() => {
    if (refundable > 0) setAmount(refundable);
  }, [refundable]);
  const refund = useMutation({ mutationFn: () => postJson(`/api/payments/${selectedPayment?.id}/refund`, { amount, reason }, token), onSuccess: onChanged });
  return <div className="crud-card"><h3>Возврат платежа</h3><CrudToolbar title="Новый возврат"><FormField label="Платеж"><select value={paymentId} onChange={(e) => setPaymentId(e.target.value)}><option value="">Выберите платеж для возврата</option>{refundablePayments.map((payment) => <option value={payment.id} key={payment.id}>{shortId(payment.id)} · {payment.type} · {payment.payableAmount - payment.refundedAmount} {payment.currency} · {payment.status}</option>)}</select></FormField><FormField label="Сумма"><input type="number" max={refundable} value={amount} onChange={(e) => setAmount(Number(e.target.value))} /></FormField><FormField label="Причина"><input value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Частичный возврат" /></FormField><ActionButton disabled={!selectedPayment || amount <= 0 || amount > refundable || !reason.trim()} pending={refund.isPending} pendingLabel="Возвращаем..." confirm={`Вернуть ${amount} ${selectedPayment?.currency ?? ""}?`} onClick={() => refund.mutate()}>Вернуть</ActionButton></CrudToolbar>{refundablePayments.length === 0 && <EmptyState label="Нет платежей, доступных для возврата" />}{validationText(amount > refundable ? "Сумма возврата больше доступной." : "")}{mutationStatus(refund, "Возврат создан")}</div>;
}

function PaymentsBoard({ payments, bookings }: { payments: Payment[]; bookings: Booking[] }) {
  const [statusFilter, setStatusFilter] = useState("ALL");
  const [typeFilter, setTypeFilter] = useState("ALL");
  const [scopeFilter, setScopeFilter] = useState("ALL");
  const visible = payments.filter((payment) => (statusFilter === "ALL" || payment.status === statusFilter) && (typeFilter === "ALL" || payment.type === typeFilter) && (scopeFilter === "ALL" || (scopeFilter === "BOOKING" ? Boolean(payment.bookingId) : Boolean(payment.orderId))));
  const totalPaid = payments.filter((payment) => payment.status === "SUCCESS" || payment.status === "PARTIALLY_REFUNDED").reduce((sum, payment) => sum + payment.payableAmount - payment.refundedAmount, 0);
  const pendingCount = payments.filter((payment) => payment.status === "PENDING").length;
  const failedCount = payments.filter((payment) => payment.status === "FAILED").length;
  const statuses = uniqueValues(payments.map((payment) => payment.status));
  const types = uniqueValues(payments.map((payment) => payment.type));
  return <div className="crud-card payments-board"><h3>Платежи</h3><div className="payment-summary"><MetricRow label="Оплачено net" value={`${Math.round(totalPaid).toLocaleString("ru-RU")} ₽`} /><MetricRow label="Pending" value={String(pendingCount)} /><MetricRow label="Failed" value={String(failedCount)} /></div><CrudToolbar title="Фильтры платежей"><FormField label="Статус"><select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)}><option value="ALL">Все статусы</option>{statuses.map((status) => <option value={status} key={status}>{status}</option>)}</select></FormField><FormField label="Тип"><select value={typeFilter} onChange={(event) => setTypeFilter(event.target.value)}><option value="ALL">Все типы</option>{types.map((type) => <option value={type} key={type}>{type}</option>)}</select></FormField><FormField label="Связь"><select value={scopeFilter} onChange={(event) => setScopeFilter(event.target.value)}><option value="ALL">Все связи</option><option value="BOOKING">Брони</option><option value="ORDER">Заказы</option></select></FormField></CrudToolbar>{visible.length === 0 ? <EmptyState label="Платежей по фильтрам нет" /> : <div className="payment-list">{visible.slice(0, 12).map((payment) => <PaymentCard payment={payment} booking={bookings.find((booking) => booking.id === payment.bookingId)} key={payment.id} />)}</div>}</div>;
}

function PaymentCard({ payment, booking }: { payment: Payment; booking?: Booking }) {
  const refundable = Math.max(0, payment.payableAmount - payment.refundedAmount);
  return <article className="payment-card"><div><strong>{shortId(payment.id)} · {payment.type}</strong><span>{payment.payableAmount} {payment.currency} · refund {payment.refundedAmount}</span><span>{payment.bookingId ? `Бронь ${shortId(payment.bookingId)}${booking ? ` · ${timeLabel(booking.startTime)}` : ""}` : payment.orderId ? `Заказ ${shortId(payment.orderId)}` : "Без связи"}</span></div><div><span className={`status ${paymentStatusTone(payment.status)}`}>{payment.status}</span><small>{payment.provider}</small><small>доступно {refundable} {payment.currency}</small></div></article>;
}

function PaymentMini({ payments }: { payments: Payment[] }) {
  if (payments.length === 0) return <div className="payment-mini empty">Платеж не создан</div>;
  return <div className="payment-mini">{payments.map((payment) => <span className={`status ${paymentStatusTone(payment.status)}`} key={payment.id}>{payment.status} · {payment.payableAmount - payment.refundedAmount} {payment.currency}</span>)}</div>;
}

function groupPaymentsByBooking(payments: Payment[]) {
  const grouped = new Map<string, Payment[]>();
  for (const payment of payments) {
    if (!payment.bookingId) continue;
    grouped.set(payment.bookingId, [...(grouped.get(payment.bookingId) ?? []), payment]);
  }
  return grouped;
}

function summarizePayments(payments: Payment[]) {
  if (payments.length === 0) return { status: "NO_PAYMENT", label: "нет платежа" };
  if (payments.some((payment) => payment.status === "SUCCESS" || payment.status === "PARTIALLY_REFUNDED")) {
    const paid = payments.filter((payment) => payment.status === "SUCCESS" || payment.status === "PARTIALLY_REFUNDED").reduce((sum, payment) => sum + payment.payableAmount - payment.refundedAmount, 0);
    return { status: "PAID", label: `${Math.round(paid).toLocaleString("ru-RU")} ₽ net` };
  }
  if (payments.some((payment) => payment.status === "PENDING")) return { status: "PENDING", label: "ожидает провайдера" };
  if (payments.every((payment) => payment.status === "FAILED")) return { status: "FAILED", label: "не прошел" };
  if (payments.every((payment) => payment.status === "REFUNDED")) return { status: "REFUNDED", label: "возвращен" };
  return { status: payments[0].status, label: `${payments.length} платежей` };
}

function paymentStatusTone(status: string) {
  if (["SUCCESS", "PAID", "CONFIRMED", "PARTIALLY_REFUNDED"].includes(status)) return "ok";
  if (["FAILED", "REFUNDED", "NO_PAYMENT"].includes(status)) return "danger";
  return "warn";
}

function uniqueValues(values: string[]) {
  return Array.from(new Set(values.filter(Boolean))).sort();
}

function InventorySection({ inventory, movements, tobaccos, token, branchId, onChanged }: { inventory: InventoryItem[]; movements: InventoryMovement[]; tobaccos: Tobacco[]; token: string; branchId: string; onChanged: () => void | Promise<unknown> }) {
  const [tobaccoId, setTobaccoId] = useState("");
  const [amountGrams, setAmountGrams] = useState(50);
  const [costPerGram, setCostPerGram] = useState(0);
  const [supplier, setSupplier] = useState("");
  const [comment, setComment] = useState("");
  const [writeOffReason, setWriteOffReason] = useState("");
  const [newStockGrams, setNewStockGrams] = useState(120);
  const [movementTobaccoId, setMovementTobaccoId] = useState("ALL");
  const [movementType, setMovementType] = useState("ALL");
  const [movementFrom, setMovementFrom] = useState(() => defaultDateInput(-14));
  const [movementTo, setMovementTo] = useState(() => defaultDateInput());
  const canStockIn = Boolean(branchId && tobaccoId && amountGrams > 0 && costPerGram >= 0);
  const canWriteOff = Boolean(branchId && tobaccoId && amountGrams > 0 && writeOffReason.trim());
  const canAdjust = Boolean(branchId && tobaccoId && newStockGrams >= 0);
  const visibleMovements = movements.filter((movement) => (movementTobaccoId === "ALL" || movement.tobaccoId === movementTobaccoId) && (movementType === "ALL" || movement.type === movementType) && localDateKey(movement.createdAt) >= movementFrom && localDateKey(movement.createdAt) <= movementTo);
  const mutation = useMutation({
    mutationFn: (type: "in" | "out") => {
      if (type === "in" && !canStockIn) throw new Error("Выберите табак, граммовку и корректную себестоимость.");
      if (type === "out" && !canWriteOff) throw new Error("Выберите табак, граммовку и причину списания.");
      return postJson(`/api/inventory/${type}`, type === "in" ? { branchId, tobaccoId, amountGrams, costPerGram, supplier: supplier || null, comment: comment || null } : { branchId, tobaccoId, amountGrams, reason: writeOffReason }, token);
    },
    onSuccess: onChanged
  });
  const adjustment = useMutation({ mutationFn: () => postJson("/api/inventory/adjustment", { branchId, tobaccoId, newStockGrams }, token), onSuccess: onChanged });
  const movementTypes = uniqueValues(movements.map((movement) => movement.type));
  return <section className="section"><div className="section-head"><h2>Склад</h2><span className="meta">in/out/adjustment, min stock и история движений</span></div><CrudToolbar title="Приход табака"><FormField label="Табак"><select value={tobaccoId} onChange={(e) => setTobaccoId(e.target.value)}><option value="">Выберите табак</option>{tobaccos.map((t) => <option value={t.id} key={t.id}>{t.brand} {t.flavor}</option>)}</select></FormField><FormField label="Граммы"><input type="number" min={0.01} value={amountGrams} onChange={(e) => setAmountGrams(Number(e.target.value))} placeholder="250" /></FormField><FormField label="Себестоимость, ₽/г"><input type="number" min={0} value={costPerGram} onChange={(e) => setCostPerGram(Number(e.target.value))} placeholder="7.5" /></FormField><FormField label="Поставщик"><input value={supplier} onChange={(e) => setSupplier(e.target.value)} placeholder="Поставщик" /></FormField><FormField label="Комментарий"><input value={comment} onChange={(e) => setComment(e.target.value)} placeholder="Новая поставка" /></FormField><button disabled={!canStockIn || mutation.isPending} onClick={() => mutation.mutate("in")}>Приход</button></CrudToolbar><CrudToolbar title="Списание"><FormField label="Табак"><select value={tobaccoId} onChange={(e) => setTobaccoId(e.target.value)}><option value="">Выберите табак</option>{tobaccos.map((t) => <option value={t.id} key={t.id}>{t.brand} {t.flavor}</option>)}</select></FormField><FormField label="Граммы"><input type="number" min={0.01} value={amountGrams} onChange={(e) => setAmountGrams(Number(e.target.value))} placeholder="20" /></FormField><FormField label="Причина"><input value={writeOffReason} onChange={(e) => setWriteOffReason(e.target.value)} placeholder="Испорчен / тестовый микс" /></FormField><button disabled={!canWriteOff || mutation.isPending} onClick={() => mutation.mutate("out")}>Списание</button></CrudToolbar><CrudToolbar title="Корректировка"><FormField label="Табак"><select value={tobaccoId} onChange={(e) => setTobaccoId(e.target.value)}><option value="">Выберите табак</option>{tobaccos.map((t) => <option value={t.id} key={t.id}>{t.brand} {t.flavor}</option>)}</select></FormField><FormField label="Новый остаток, г"><input type="number" min={0} value={newStockGrams} onChange={(e) => setNewStockGrams(Number(e.target.value))} placeholder="120" /></FormField><button disabled={!canAdjust || adjustment.isPending} onClick={() => adjustment.mutate()}>Корректировка</button></CrudToolbar>{mutationStatus(mutation, "Операция склада выполнена")}{mutationStatus(adjustment, "Остаток скорректирован")}<div className="inventory-grid">{inventory.length === 0 ? <EmptyState label="Склад пуст" /> : inventory.map((item) => <InventoryStockCard key={item.id} item={item} tobacco={tobaccos.find((t) => t.id === item.tobaccoId)} token={token} onChanged={onChanged} />)}</div><div className="crud-card"><h3>История движений</h3><CrudToolbar><FormField label="Табак"><select value={movementTobaccoId} onChange={(event) => setMovementTobaccoId(event.target.value)}><option value="ALL">Все табаки</option>{tobaccos.map((tobacco) => <option value={tobacco.id} key={tobacco.id}>{tobacco.brand} {tobacco.flavor}</option>)}</select></FormField><FormField label="Тип"><select value={movementType} onChange={(event) => setMovementType(event.target.value)}><option value="ALL">Все типы</option>{movementTypes.map((type) => <option value={type} key={type}>{type}</option>)}</select></FormField><FormField label="От"><input type="date" value={movementFrom} onChange={(event) => setMovementFrom(event.target.value)} /></FormField><FormField label="До"><input type="date" value={movementTo} onChange={(event) => setMovementTo(event.target.value)} /></FormField></CrudToolbar>{visibleMovements.length === 0 ? <EmptyState label="Движений по фильтрам нет" /> : <div className="queue cards">{visibleMovements.slice(0, 80).map((movement) => <article className="booking-row" key={movement.id}><div><strong>{movement.type} · {movement.amountGrams} г</strong><div className="meta">{tobaccos.find((tobacco) => tobacco.id === movement.tobaccoId)?.flavor ?? shortId(movement.tobaccoId)} · {movement.reason ?? "без причины"}</div></div><span>{timeLabel(movement.createdAt)}</span><span className="meta">{movement.orderId ? `order ${shortId(movement.orderId)}` : "manual"}</span></article>)}</div>}</div></section>;
}

function InventoryStockCard({ item, tobacco, token, onChanged }: { item: InventoryItem; tobacco?: Tobacco; token: string; onChanged: () => void | Promise<unknown> }) {
  const [minStockGrams, setMinStockGrams] = useState(item.minStockGrams);
  const update = useMutation({ mutationFn: () => patchJson(`/api/inventory/${item.id}`, { minStockGrams }, token), onSuccess: onChanged });
  const low = item.stockGrams < item.minStockGrams;
  return <article className={`stock-card ${low ? "low" : ""}`}><strong>{tobacco ? `${tobacco.brand} ${tobacco.flavor}` : item.tobaccoId}</strong><div className="bar"><span style={{ width: `${Math.min(100, (item.stockGrams / Math.max(1, item.minStockGrams * 3)) * 100)}%` }} /></div><div className="stock-line"><span>{item.stockGrams} г</span><span>min {item.minStockGrams} г</span></div><CrudToolbar title="Порог"><FormField label="Минимум, г"><input type="number" min={0} value={minStockGrams} onChange={(event) => setMinStockGrams(Number(event.target.value))} /></FormField><button disabled={minStockGrams < 0 || update.isPending} onClick={() => update.mutate()}>min stock</button></CrudToolbar><FieldError message={low ? "Ниже минимального остатка" : ""} />{mutationStatus(update, "Минимальный остаток обновлен")}</article>;
}

type MixInputDraft = { tobaccoId: string; percent: number };

function defaultMixInputDraft(): MixInputDraft[] {
  return [{ tobaccoId: "", percent: 50 }, { tobaccoId: "", percent: 50 }];
}

function MixItemsEditor({ items, tobaccos, onChange }: { items: MixInputDraft[]; tobaccos: Tobacco[]; onChange: (items: MixInputDraft[]) => void }) {
  const total = items.reduce((sum, item) => sum + Number(item.percent || 0), 0);
  const updateItem = (index: number, patch: Partial<MixInputDraft>) => onChange(items.map((item, current) => current === index ? { ...item, ...patch } : item));
  const addItem = () => onChange([...items, { tobaccoId: "", percent: 0 }]);
  const removeItem = (index: number) => onChange(items.filter((_, current) => current !== index));
  const distribute = () => {
    const percent = Math.floor(100 / Math.max(1, items.length));
    const next = items.map((item, index) => ({ ...item, percent: index === items.length - 1 ? 100 - percent * (items.length - 1) : percent }));
    onChange(next);
  };
  return <div className="mix-items-editor"><div className="section-head compact"><h4>Состав микса</h4><span className={`status ${total === 100 ? "ok" : "warn"}`}>Итого {total}%</span></div>{items.map((item, index) => <CrudToolbar title={`Табак ${index + 1}`} key={index}><FormField label="Табак"><select value={item.tobaccoId} onChange={(event) => updateItem(index, { tobaccoId: event.target.value })}><option value="">Табак</option>{tobaccos.map((tobacco) => <option value={tobacco.id} key={tobacco.id}>{tobacco.brand} {tobacco.flavor}</option>)}</select></FormField><FormField label="Процент"><input type="number" min={1} max={100} value={item.percent} onChange={(event) => updateItem(index, { percent: Number(event.target.value) })} /></FormField><button className="mini-button danger" disabled={items.length <= 1} onClick={() => removeItem(index)}>remove</button></CrudToolbar>)}<div className="action-row"><button className="mini-button" onClick={addItem}>Добавить табак</button><button className="mini-button" onClick={distribute}>Разделить 100%</button></div></div>;
}

function normalizeMixItems(items: MixInputDraft[]) {
  return items.map((item) => ({ tobaccoId: item.tobaccoId, percent: Number(item.percent) }));
}

function validateMixDraft({ name, bowlId, tasteProfile, price, items }: { name: string; bowlId: string; tasteProfile: string; price: number; items: MixInputDraft[] }) {
  const normalized = normalizeMixItems(items);
  const tobaccoIds = normalized.map((item) => item.tobaccoId).filter(Boolean);
  const uniqueTobaccoIds = new Set(tobaccoIds);
  const total = normalized.reduce((sum, item) => sum + item.percent, 0);
  if (!name.trim()) return "Введите название микса.";
  if (!bowlId) return "Выберите чашку.";
  if (!tasteProfile.trim()) return "Укажите вкусовой профиль.";
  if (price < 0) return "Цена не может быть отрицательной.";
  if (normalized.length === 0 || tobaccoIds.length !== normalized.length) return "Заполните табак в каждой строке состава.";
  if (uniqueTobaccoIds.size !== tobaccoIds.length) return "Один табак нельзя добавить в микс дважды.";
  if (normalized.some((item) => item.percent <= 0)) return "Процент каждой строки должен быть больше 0.";
  if (total !== 100) return "Сумма процентов микса должна быть ровно 100%.";
  return "";
}

function MixologySection({ mixes, bowls, tobaccos, token, onChanged }: { mixes: Mix[]; bowls: Bowl[]; tobaccos: Tobacco[]; token: string; onChanged: () => void }) {
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [bowlId, setBowlId] = useState("");
  const [strength, setStrength] = useState("MEDIUM");
  const [tasteProfile, setTasteProfile] = useState("");
  const [price, setPrice] = useState(0);
  const [isPublic, setIsPublic] = useState(false);
  const [items, setItems] = useState<MixInputDraft[]>(defaultMixInputDraft());
  const mixValidation = validateMixDraft({ name, bowlId, tasteProfile, price, items });
  const canSubmit = !mixValidation;
  const mutation = useMutation({
    mutationFn: () => {
      if (!canSubmit) throw new Error(mixValidation || "Проверьте состав микса.");
      return postJson("/api/mixes", { name: name.trim(), description: description.trim() || null, bowlId, strength: strength.trim(), tasteProfile: tasteProfile.trim(), price, isPublic, items: normalizeMixItems(items) }, token);
    },
    onSuccess: async () => { setName(""); setDescription(""); setBowlId(""); setStrength("MEDIUM"); setTasteProfile(""); setPrice(0); setIsPublic(false); setItems(defaultMixInputDraft()); await onChanged(); }
  });
  return <section className="section"><div className="section-head"><h2>Миксология</h2><span className="meta">bowls, tobaccos, mixes CRUD без ограничения на 2 табака</span></div><div className="crud-grid"><BowlCrud bowls={bowls} token={token} onChanged={onChanged} /><TobaccoCrud tobaccos={tobaccos} token={token} onChanged={onChanged} /></div><div className="crud-card"><h3>Миксы</h3><CrudToolbar title="Новый микс"><FormField label="Название"><input value={name} onChange={(e) => setName(e.target.value)} placeholder="Berry Ice" /></FormField><FormField label="Описание"><input value={description} onChange={(e) => setDescription(e.target.value)} placeholder="Ягодно-свежий микс" /></FormField><FormField label="Чашка"><select value={bowlId} onChange={(e) => setBowlId(e.target.value)}><option value="">Выберите чашку</option>{bowls.map((bowl) => <option value={bowl.id} key={bowl.id}>{bowl.name}</option>)}</select></FormField><FormField label="Крепость"><input value={strength} onChange={(e) => setStrength(e.target.value)} placeholder="MEDIUM" /></FormField><FormField label="Профиль вкуса"><input value={tasteProfile} onChange={(e) => setTasteProfile(e.target.value)} placeholder="BERRY_FRESH" /></FormField><FormField label="Цена"><input type="number" min={0} value={price} onChange={(e) => setPrice(Number(e.target.value))} placeholder="1500" /></FormField><label className="check"><input type="checkbox" checked={isPublic} onChange={(e) => setIsPublic(e.target.checked)} />public</label><button disabled={!canSubmit || mutation.isPending} onClick={() => mutation.mutate()}>Создать микс</button></CrudToolbar><MixItemsEditor items={items} tobaccos={tobaccos} onChange={setItems} />{validationText(mixValidation)}{mutationStatus(mutation, "Микс создан")}<div className="mix-grid">{mixes.length === 0 ? <EmptyState label="Миксов нет" /> : mixes.map((mix) => <MixRow key={mix.id} mix={mix} bowls={bowls} tobaccos={tobaccos} token={token} onChanged={onChanged} />)}</div></div></section>;
}

function BowlCrud({ bowls, token, onChanged }: { bowls: Bowl[]; token: string; onChanged: () => void }) {
  const [name, setName] = useState("");
  const [type, setType] = useState("");
  const [capacityGrams, setCapacityGrams] = useState(0);
  const [recommendedStrength, setRecommendedStrength] = useState("");
  const [averageSmokeMinutes, setAverageSmokeMinutes] = useState(0);
  const canCreate = Boolean(name.trim() && type.trim() && capacityGrams > 0 && recommendedStrength.trim() && averageSmokeMinutes > 0);
  const create = useMutation({ mutationFn: () => postJson("/api/bowls", { name: name.trim(), type: type.trim(), capacityGrams, recommendedStrength: recommendedStrength.trim(), averageSmokeMinutes }, token), onSuccess: async () => { setName(""); setType(""); setCapacityGrams(0); setRecommendedStrength(""); setAverageSmokeMinutes(0); await onChanged(); } });
  return <div className="crud-card"><h3>Чашки</h3><CrudToolbar title="Новая чашка"><FormField label="Название"><input value={name} onChange={(e) => setName(e.target.value)} placeholder="Oblako Phunnel M" /></FormField><FormField label="Тип"><input value={type} onChange={(e) => setType(e.target.value)} placeholder="PHUNNEL" /></FormField><FormField label="Граммы"><input type="number" min={1} value={capacityGrams} onChange={(e) => setCapacityGrams(Number(e.target.value))} placeholder="18" /></FormField><FormField label="Крепость"><input value={recommendedStrength} onChange={(e) => setRecommendedStrength(e.target.value)} placeholder="MEDIUM" /></FormField><FormField label="Минуты"><input type="number" min={1} value={averageSmokeMinutes} onChange={(e) => setAverageSmokeMinutes(Number(e.target.value))} placeholder="70" /></FormField><button disabled={!canCreate || create.isPending} onClick={() => create.mutate()}>Создать</button></CrudToolbar>{mutationStatus(create, "Чашка создана")}{bowls.length === 0 ? <EmptyState label="Чашек нет" /> : bowls.map((bowl) => <BowlRow key={bowl.id} bowl={bowl} token={token} onChanged={onChanged} />)}</div>;
}

function BowlRow({ bowl, token, onChanged }: { bowl: Bowl; token: string; onChanged: () => void }) {
  const [name, setName] = useState(bowl.name);
  const [type, setType] = useState(bowl.type);
  const [capacityGrams, setCapacityGrams] = useState(bowl.capacityGrams);
  const [recommendedStrength, setRecommendedStrength] = useState(bowl.recommendedStrength);
  const [averageSmokeMinutes, setAverageSmokeMinutes] = useState(bowl.averageSmokeMinutes);
  const validation = !name.trim() ? "Название чашки обязательно." : !type.trim() ? "Тип обязателен." : capacityGrams <= 0 ? "Граммовка должна быть больше 0." : !recommendedStrength.trim() ? "Крепость обязательна." : averageSmokeMinutes <= 0 ? "Время курения должно быть больше 0." : "";
  const update = useMutation({ mutationFn: () => patchJson(`/api/bowls/${bowl.id}`, { name: name.trim(), type: type.trim(), capacityGrams, recommendedStrength: recommendedStrength.trim(), averageSmokeMinutes, isActive: true }, token), onSuccess: onChanged });
  const remove = useMutation({ mutationFn: () => deleteJson(`/api/bowls/${bowl.id}`, undefined, token), onSuccess: onChanged });
  return <div className="crud-row-block"><div className="crud-row wide-row"><FormField label="Название"><input value={name} onChange={(e) => setName(e.target.value)} /></FormField><FormField label="Тип"><input value={type} onChange={(e) => setType(e.target.value)} /></FormField><FormField label="Граммы"><input type="number" min={1} value={capacityGrams} onChange={(e) => setCapacityGrams(Number(e.target.value))} /></FormField><FormField label="Крепость"><input value={recommendedStrength} onChange={(e) => setRecommendedStrength(e.target.value)} /></FormField><FormField label="Минуты"><input type="number" min={1} value={averageSmokeMinutes} onChange={(e) => setAverageSmokeMinutes(Number(e.target.value))} /></FormField><CrudRowActions><ActionButton disabled={Boolean(validation)} pending={update.isPending} pendingLabel="Сохраняем..." onClick={() => update.mutate()}>save</ActionButton><ActionButton danger pending={remove.isPending} pendingLabel="Деактивируем..." confirm="Деактивировать чашку?" onClick={() => remove.mutate()}>delete</ActionButton></CrudRowActions></div>{validationText(validation)}{mutationStatus(update, "Чашка обновлена")}{mutationStatus(remove, "Чашка деактивирована")}</div>;
}

function TobaccoCrud({ tobaccos, token, onChanged }: { tobaccos: Tobacco[]; token: string; onChanged: () => void }) {
  const [brand, setBrand] = useState("");
  const [line, setLine] = useState("");
  const [flavor, setFlavor] = useState("");
  const [strength, setStrength] = useState("");
  const [category, setCategory] = useState("");
  const [description, setDescription] = useState("");
  const [photoUrl, setPhotoUrl] = useState("");
  const [costPerGram, setCostPerGram] = useState(0);
  const canCreate = Boolean(brand.trim() && flavor.trim() && strength.trim() && category.trim() && costPerGram >= 0);
  const create = useMutation({ mutationFn: () => postJson("/api/tobaccos", { brand: brand.trim(), line: line.trim(), flavor: flavor.trim(), strength: strength.trim(), category: category.trim(), description: description.trim() || null, costPerGram, photoUrl: photoUrl.trim() || null }, token), onSuccess: async () => { setBrand(""); setLine(""); setFlavor(""); setStrength(""); setCategory(""); setDescription(""); setPhotoUrl(""); setCostPerGram(0); await onChanged(); } });
  return <div className="crud-card"><h3>Табаки</h3><CrudToolbar title="Новый табак"><FormField label="Бренд"><input value={brand} onChange={(e) => setBrand(e.target.value)} placeholder="Darkside" /></FormField><FormField label="Линейка"><input value={line} onChange={(e) => setLine(e.target.value)} placeholder="Base" /></FormField><FormField label="Вкус"><input value={flavor} onChange={(e) => setFlavor(e.target.value)} placeholder="Strawberry" /></FormField><FormField label="Крепость"><input value={strength} onChange={(e) => setStrength(e.target.value)} placeholder="STRONG" /></FormField><FormField label="Категория"><input value={category} onChange={(e) => setCategory(e.target.value)} placeholder="BERRY" /></FormField><FormField label="Описание"><input value={description} onChange={(e) => setDescription(e.target.value)} placeholder="Клубничный вкус" /></FormField><FormField label="Фото URL"><input value={photoUrl} onChange={(e) => setPhotoUrl(e.target.value)} placeholder="https://..." /></FormField><FormField label="₽/г"><input type="number" value={costPerGram} onChange={(e) => setCostPerGram(Number(e.target.value))} placeholder="8.5" /></FormField><button disabled={!canCreate || create.isPending} onClick={() => create.mutate()}>Создать</button></CrudToolbar>{mutationStatus(create, "Табак создан")}{tobaccos.length === 0 ? <EmptyState label="Табаков нет" /> : tobaccos.map((tobacco) => <TobaccoRow key={tobacco.id} tobacco={tobacco} token={token} onChanged={onChanged} />)}</div>;
}

function TobaccoRow({ tobacco, token, onChanged }: { tobacco: Tobacco; token: string; onChanged: () => void }) {
  const [brand, setBrand] = useState(tobacco.brand);
  const [line, setLine] = useState(tobacco.line ?? "");
  const [flavor, setFlavor] = useState(tobacco.flavor);
  const [strength, setStrength] = useState(tobacco.strength);
  const [category, setCategory] = useState(tobacco.category);
  const [description, setDescription] = useState(tobacco.description ?? "");
  const [photoUrl, setPhotoUrl] = useState(tobacco.photoUrl ?? "");
  const [costPerGram, setCostPerGram] = useState(tobacco.costPerGram);
  const validation = !brand.trim() ? "Бренд обязателен." : !flavor.trim() ? "Вкус обязателен." : !strength.trim() ? "Крепость обязательна." : !category.trim() ? "Категория обязательна." : costPerGram < 0 ? "Себестоимость не может быть отрицательной." : "";
  const update = useMutation({ mutationFn: () => patchJson(`/api/tobaccos/${tobacco.id}`, { brand: brand.trim(), line: line.trim(), flavor: flavor.trim(), strength: strength.trim(), category: category.trim(), description: description.trim() || null, costPerGram, isActive: true, photoUrl: photoUrl.trim() || null }, token), onSuccess: onChanged });
  const remove = useMutation({ mutationFn: () => deleteJson(`/api/tobaccos/${tobacco.id}`, undefined, token), onSuccess: onChanged });
  return <div className="crud-row-block"><div className="crud-row wide-row"><FormField label="Бренд"><input value={brand} onChange={(e) => setBrand(e.target.value)} placeholder="Бренд" /></FormField><FormField label="Линейка"><input value={line} onChange={(e) => setLine(e.target.value)} placeholder="Линейка" /></FormField><FormField label="Вкус"><input value={flavor} onChange={(e) => setFlavor(e.target.value)} placeholder="Вкус" /></FormField><FormField label="Крепость"><input value={strength} onChange={(e) => setStrength(e.target.value)} placeholder="Крепость" /></FormField><FormField label="Категория"><input value={category} onChange={(e) => setCategory(e.target.value)} placeholder="Категория" /></FormField><FormField label="Описание"><input value={description} onChange={(e) => setDescription(e.target.value)} placeholder="Описание" /></FormField><FormField label="Фото URL"><input value={photoUrl} onChange={(e) => setPhotoUrl(e.target.value)} placeholder="Фото URL" /></FormField><FormField label="₽/г"><input type="number" min={0} value={costPerGram} onChange={(e) => setCostPerGram(Number(e.target.value))} /></FormField><CrudRowActions><ActionButton disabled={Boolean(validation)} pending={update.isPending} pendingLabel="Сохраняем..." onClick={() => update.mutate()}>save</ActionButton><ActionButton danger pending={remove.isPending} pendingLabel="Деактивируем..." confirm="Деактивировать табак?" onClick={() => remove.mutate()}>delete</ActionButton></CrudRowActions></div>{validationText(validation)}{mutationStatus(update, "Табак обновлен")}{mutationStatus(remove, "Табак деактивирован")}</div>;
}

function MixRow({ mix, bowls, tobaccos, token, onChanged }: { mix: Mix; bowls: Bowl[]; tobaccos: Tobacco[]; token: string; onChanged: () => void }) {
  const [name, setName] = useState(mix.name);
  const [description, setDescription] = useState(mix.description ?? "");
  const [bowlId, setBowlId] = useState(mix.bowlId);
  const [strength, setStrength] = useState(mix.strength);
  const [tasteProfile, setTasteProfile] = useState(mix.tasteProfile);
  const [price, setPrice] = useState(mix.price);
  const [isPublic, setIsPublic] = useState(mix.isPublic);
  const [items, setItems] = useState<MixInputDraft[]>(mix.items?.length ? mix.items.map((item) => ({ tobaccoId: item.tobaccoId, percent: item.percent })) : defaultMixInputDraft());
  const validation = !strength.trim() ? "Крепость обязательна." : validateMixDraft({ name, bowlId, tasteProfile, price, items });
  const update = useMutation({ mutationFn: () => patchJson(`/api/mixes/${mix.id}`, { name: name.trim(), description: description.trim() || null, bowlId, strength: strength.trim(), tasteProfile: tasteProfile.trim(), price, isPublic, isActive: true, items: normalizeMixItems(items) }, token), onSuccess: onChanged });
  const remove = useMutation({ mutationFn: () => deleteJson(`/api/mixes/${mix.id}`, undefined, token), onSuccess: onChanged });
  return <article className="mix-card"><div className="crud-row wide-row"><FormField label="Название"><input value={name} onChange={(e) => setName(e.target.value)} /></FormField><FormField label="Описание"><input value={description} onChange={(e) => setDescription(e.target.value)} /></FormField><FormField label="Чашка"><select value={bowlId} onChange={(e) => setBowlId(e.target.value)}><option value="">Выберите чашку</option>{bowls.map((bowl) => <option value={bowl.id} key={bowl.id}>{bowl.name}</option>)}</select></FormField><FormField label="Крепость"><input value={strength} onChange={(e) => setStrength(e.target.value)} /></FormField><FormField label="Профиль"><input value={tasteProfile} onChange={(e) => setTasteProfile(e.target.value)} /></FormField><FormField label="Цена"><input type="number" min={0} value={price} onChange={(e) => setPrice(Number(e.target.value))} /></FormField><label className="check"><input type="checkbox" checked={isPublic} onChange={(e) => setIsPublic(e.target.checked)} />public</label><CrudRowActions><ActionButton disabled={Boolean(validation)} pending={update.isPending} pendingLabel="Сохраняем..." onClick={() => update.mutate()}>save</ActionButton><ActionButton danger pending={remove.isPending} pendingLabel="Деактивируем..." confirm="Деактивировать микс?" onClick={() => remove.mutate()}>delete</ActionButton></CrudRowActions></div><MixItemsEditor items={items} tobaccos={tobaccos} onChange={setItems} /><div className="pill-row"><span>{mix.totalGrams} г</span><span>{mix.price} ₽</span><span>cost {mix.cost ?? "-"}</span><span>margin {mix.margin ?? "-"}</span></div>{validationText(validation)}{mutationStatus(update, "Микс обновлен")}{mutationStatus(remove, "Микс удален")}</article>;
}

function StaffSection({ token, branchId, users, shifts, onChanged }: { token: string; branchId: string; users: UserProfile[]; shifts: StaffShift[]; onChanged: () => void | Promise<unknown> }) {
  const [name, setName] = useState("");
  const [phone, setPhone] = useState("");
  const [role, setRole] = useState<RoleCode>("WAITER");
  const [roleFilter, setRoleFilter] = useState("ALL");
  const [staffId, setStaffId] = useState("");
  const [staffFilter, setStaffFilter] = useState("ALL");
  const [shiftCancelReason, setShiftCancelReason] = useState("");
  const [weekStart, setWeekStart] = useState(() => startOfWeekInput());
  const [startsAt, setStartsAt] = useState(() => defaultDateTimeInput(18));
  const [endsAt, setEndsAt] = useState(() => defaultDateTimeInput(26));
  const selectedStaffId = staffId;
  const shiftDateError = !dateRangeIsValid(startsAt, endsAt) ? "Окончание смены должно быть позже начала." : "";
  const weekDays = useMemo(() => buildWeekDays(weekStart), [weekStart]);
  const visibleShifts = useMemo(() => shifts.filter((shift) => weekDays.includes(localDateKey(shift.startsAt)) && (roleFilter === "ALL" || shift.roleOnShift === roleFilter) && (staffFilter === "ALL" || shift.staffId === staffFilter)), [roleFilter, shifts, staffFilter, weekDays]);
  const staffValidation = !name.trim() ? "Введите имя сотрудника." : !isValidPhone(phone) ? "Телефон должен быть в формате +79990000000." : "";
  const selectScheduleDay = (day: string) => {
    setStartsAt(`${day}T18:00`);
    setEndsAt(`${addDaysInput(day, 1)}T02:00`);
  };
  const createStaff = useMutation({ mutationFn: () => postJson("/api/users/staff", { name: name.trim(), phone: normalizePhone(phone), role, branchId }, token), onSuccess: async () => { setName(""); setPhone(""); await onChanged(); } });
  const createShift = useMutation({ mutationFn: () => postJson("/api/staff/shifts", { staffId: selectedStaffId, branchId, startsAt: new Date(startsAt).toISOString(), endsAt: new Date(endsAt).toISOString(), roleOnShift: role }, token), onSuccess: onChanged });
  const shiftAction = useMutation({ mutationFn: ({ id, action }: { id: string; action: "start" | "finish" | "cancel" }) => {
    if (action === "cancel" && !shiftCancelReason.trim()) throw new Error("Укажите причину отмены смены.");
    return patchJson(`/api/staff/shifts/${id}/${action}`, action === "cancel" ? { reason: shiftCancelReason } : {}, token);
  }, onSuccess: onChanged });
  return <section className="section staff-board"><div><Gauge size={22} /><h2>Персонал</h2><p>Сотрудники, недельный график и статусы смен закрыты permission `staff.manage`.</p></div><CrudToolbar title="Новый сотрудник"><FormField label="Имя"><input value={name} onChange={(e) => setName(e.target.value)} placeholder="Иван" /></FormField><FormField label="Телефон"><input type="tel" inputMode="tel" value={phone} onChange={(e) => setPhone(normalizePhoneInput(e.target.value))} placeholder="+79990000000" /></FormField><FormField label="Роль"><select value={role} onChange={(e) => setRole(e.target.value as RoleCode)}><option value="MANAGER">Manager</option><option value="HOOKAH_MASTER">Hookah master</option><option value="WAITER">Waiter</option></select></FormField><button disabled={Boolean(staffValidation) || createStaff.isPending} onClick={() => createStaff.mutate()}>Создать сотрудника</button></CrudToolbar>{validationText(staffValidation)}{mutationStatus(createStaff, "Сотрудник создан")}<div className="crud-card"><h3>Сотрудники</h3>{users.length === 0 ? <EmptyState label="Сотрудников нет" /> : users.map((user) => <StaffUserRow key={user.id} user={user} branchId={branchId} token={token} onChanged={onChanged} />)}</div><div className="schedule-toolbar"><button onClick={() => setWeekStart(addDaysInput(weekStart, -7))}>← неделя</button><label><span>Начало недели</span><input type="date" value={weekStart} onChange={(event) => setWeekStart(startOfWeekInput(event.target.value))} /></label><button onClick={() => setWeekStart(addDaysInput(weekStart, 7))}>неделя →</button><select value={roleFilter} onChange={(event) => setRoleFilter(event.target.value)}><option value="ALL">Все роли</option><option value="MANAGER">Manager</option><option value="HOOKAH_MASTER">Hookah master</option><option value="WAITER">Waiter</option></select><select value={staffFilter} onChange={(event) => setStaffFilter(event.target.value)}><option value="ALL">Все сотрудники</option>{users.map((user) => <option value={user.id} key={user.id}>{user.name}</option>)}</select></div><div className="schedule-grid">{weekDays.map((day) => { const dayShifts = visibleShifts.filter((shift) => localDateKey(shift.startsAt) === day); return <article className="schedule-day" key={day}><button className="schedule-day-head" onClick={() => selectScheduleDay(day)}><strong>{weekdayLabel(day)}</strong><span>{day}</span></button>{dayShifts.length === 0 ? <p className="meta">Смен нет</p> : dayShifts.map((shift) => <div className={`schedule-shift ${shift.status.toLowerCase()}`} key={shift.id}><strong>{users.find((user) => user.id === shift.staffId)?.name ?? shortId(shift.staffId)}</strong><span>{timeLabel(shift.startsAt)} - {timeLabel(shift.endsAt)}</span><em>{shift.roleOnShift ?? "role"} · {shift.status}</em></div>)}</article>; })}</div><CrudToolbar title="Новая смена"><FormField label="Сотрудник"><select value={selectedStaffId} onChange={(e) => setStaffId(e.target.value)}><option value="">Выберите сотрудника</option>{users.map((user) => <option value={user.id} key={user.id}>{user.name} · {user.role}</option>)}</select></FormField><FormField label="Роль на смене"><select value={role} onChange={(e) => setRole(e.target.value as RoleCode)}><option value="MANAGER">Manager</option><option value="HOOKAH_MASTER">Hookah master</option><option value="WAITER">Waiter</option></select></FormField><FormField label="Начало"><input type="datetime-local" value={startsAt} onChange={(e) => setStartsAt(e.target.value)} /></FormField><FormField label="Окончание"><input type="datetime-local" value={endsAt} onChange={(e) => setEndsAt(e.target.value)} /></FormField><FormField label="Причина отмены"><input value={shiftCancelReason} onChange={(e) => setShiftCancelReason(e.target.value)} placeholder="Для отмены смены" /></FormField><button disabled={!selectedStaffId || Boolean(shiftDateError) || createShift.isPending} onClick={() => createShift.mutate()}>Создать смену</button></CrudToolbar>{validationText(shiftDateError)}{mutationStatus(createShift, "Смена создана")}{mutationStatus(shiftAction, "Статус смены обновлен")}<div className="queue cards">{visibleShifts.length === 0 ? <EmptyState label="Смены по фильтрам не найдены" /> : visibleShifts.map((shift) => <article className="booking-row" key={shift.id}><div><strong>{users.find((user) => user.id === shift.staffId)?.name ?? shortId(shift.staffId)}</strong><div className="meta">{timeLabel(shift.startsAt)} - {timeLabel(shift.endsAt)} · {shift.roleOnShift ?? "role"}</div></div><span className={`status ${shift.status === "ACTIVE" ? "ok" : "warn"}`}>{shift.status}</span><div className="row-actions"><button onClick={() => shiftAction.mutate({ id: shift.id, action: "start" })}>start</button><button onClick={() => shiftAction.mutate({ id: shift.id, action: "finish" })}>finish</button><ActionButton danger disabled={!shiftCancelReason.trim()} pending={shiftAction.isPending} pendingLabel="Отменяем..." confirm="Отменить смену?" onClick={() => shiftAction.mutate({ id: shift.id, action: "cancel" })}>cancel</ActionButton></div></article>)}</div></section>;
}

function StaffUserRow({ user, branchId, token, onChanged }: { user: UserProfile; branchId: string; token: string; onChanged: () => void | Promise<unknown> }) {
  const [name, setName] = useState(user.name);
  const [email, setEmail] = useState(user.email ?? "");
  const [status, setStatus] = useState(user.status);
  const validation = !name.trim() ? "Введите имя сотрудника." : !isValidEmail(email) ? "Email должен быть корректным." : "";
  const update = useMutation({ mutationFn: () => patchJson(`/api/users/${user.id}`, { name: name.trim(), email: email.trim() || null, branchId, status }, token), onSuccess: onChanged });
  const remove = useMutation({ mutationFn: () => deleteJson(`/api/users/${user.id}`, undefined, token), onSuccess: onChanged });
  return <div className="crud-row-block"><div className="crud-row"><FormField label="Имя"><input value={name} onChange={(e) => setName(e.target.value)} placeholder="Имя" /></FormField><FormField label="Email"><input type="email" value={email} onChange={(e) => setEmail(e.target.value)} placeholder="email@domain.ru" /></FormField><FormField label="Статус"><select value={status} onChange={(e) => setStatus(e.target.value)}><option value="active">active</option><option value="inactive">inactive</option><option value="blocked">blocked</option></select></FormField><span className="meta">{user.role}</span><CrudRowActions><ActionButton disabled={Boolean(validation)} pending={update.isPending} pendingLabel="Сохраняем..." onClick={() => update.mutate()}>save</ActionButton><ActionButton danger pending={remove.isPending} pendingLabel="Деактивируем..." confirm="Деактивировать сотрудника?" onClick={() => remove.mutate()}>delete</ActionButton></CrudRowActions></div>{validationText(validation)}{mutationStatus(update, "Сотрудник обновлен")}{mutationStatus(remove, "Сотрудник деактивирован")}</div>;
}


type QueryState = { isLoading: boolean; isFetching: boolean; isError: boolean; error: unknown };

function AnalyticsSection({ metrics, metricsQuery, topMixes, topMixesQuery, tobaccoUsage, tobaccoUsageQuery, staffPerformance, staffPerformanceQuery, tableLoad, tableLoadQuery, mixes, tobaccos, staff, tables, from, to, onRange }: { metrics?: DashboardMetrics; metricsQuery: QueryState; topMixes: TopMixMetric[]; topMixesQuery: QueryState; tobaccoUsage: TobaccoUsageMetric[]; tobaccoUsageQuery: QueryState; staffPerformance: StaffPerformanceMetric[]; staffPerformanceQuery: QueryState; tableLoad: TableLoadMetric[]; tableLoadQuery: QueryState; mixes: Mix[]; tobaccos: Tobacco[]; staff: UserProfile[]; tables: Table[]; from: string; to: string; onRange: (from: string, to: string) => void }) {
  const rangeInvalid = !from || !to || from > to;
  const setPreset = (days: number) => onRange(defaultDateInput(-days), defaultDateInput());
  const mixRows = topMixes.slice(0, 8).map((item) => ({ ...item, label: mixes.find((mix) => mix.id === item.mixId)?.name ?? item.name ?? shortId(item.mixId) }));
  const tobaccoRows = tobaccoUsage.slice(0, 8).map((item) => {
    const tobacco = tobaccos.find((candidate) => candidate.id === item.tobaccoId);
    return { ...item, amount: item.amountGrams ?? item.grams ?? 0, label: tobacco ? `${tobacco.brand} ${tobacco.flavor}` : shortId(item.tobaccoId) };
  });
  const staffRows = staffPerformance.slice(0, 8).map((item) => ({ ...item, label: staff.find((candidate) => candidate.id === item.staffId)?.name ?? item.staffName ?? shortId(item.staffId) }));
  const tableRows = tableLoad.slice(0, 8).map((item) => ({ ...item, label: tables.find((candidate) => candidate.id === item.tableId)?.name ?? item.tableName ?? shortId(item.tableId) }));
  const topMixMax = Math.max(...mixRows.map((item) => item.ordersCount), 1);
  const tobaccoMax = Math.max(...tobaccoRows.map((item) => item.amount), 1);
  const staffMax = Math.max(...staffRows.map((item) => item.ordersServed), 1);
  const bestMix = mixRows[0];
  const biggestUsage = tobaccoRows[0];
  const bestStaff = staffRows[0];
  const busiestTable = tableRows[0];

  return <section className="section"><div className="section-head"><h2>Аналитика</h2><span className="meta">фильтры, графики, drill-down по миксам, складу, персоналу и столам</span></div><div className="analytics-toolbar"><button onClick={() => setPreset(7)}>7 дней</button><button onClick={() => setPreset(30)}>30 дней</button><button onClick={() => setPreset(90)}>90 дней</button><label><span>От</span><input type="date" value={from} onChange={(event) => onRange(event.target.value, to)} /></label><label><span>До</span><input type="date" value={to} onChange={(event) => onRange(from, event.target.value)} /></label></div><FieldError message={rangeInvalid ? "Диапазон аналитики некорректен: дата начала должна быть раньше даты окончания." : ""} /><ChartWidget title="KPI периода" query={metricsQuery} empty={!metrics}><div className="analytics-grid"><article><span>Выручка</span><strong>{formatRub(metrics?.revenue ?? 0)}</strong></article><article><span>Заказы</span><strong>{metrics?.ordersCount ?? 0}</strong></article><article><span>Средний чек</span><strong>{formatRub(metrics?.averageCheck ?? 0)}</strong></article><article><span>No-show</span><strong>{metrics?.noShowRate ?? 0}%</strong></article></div><div className="chart-card nested"><h3>Операционные метрики</h3><Bar label="Заказы" value={metrics?.ordersCount ?? 0} max={Math.max(1, metrics?.ordersCount ?? 1, metrics?.bookingsCount ?? 1)} /><Bar label="Брони" value={metrics?.bookingsCount ?? 0} max={Math.max(1, metrics?.ordersCount ?? 1, metrics?.bookingsCount ?? 1)} /><Bar label="No-show" value={metrics?.noShowRate ?? 0} max={100} suffix="%" /></div></ChartWidget><div className="drilldown-grid"><DrilldownCard title="Лидер миксов" value={bestMix ? bestMix.label : "нет данных"} meta={bestMix ? `${bestMix.ordersCount} заказов · ${formatRub(bestMix.revenue)}` : "за период"} /><DrilldownCard title="Расход табака" value={biggestUsage ? biggestUsage.label : "нет данных"} meta={biggestUsage ? `${Math.round(biggestUsage.amount)} г` : "агрегат склада"} /><DrilldownCard title="Персонал" value={bestStaff ? bestStaff.label : "нет данных"} meta={bestStaff ? `${bestStaff.ordersServed} заказов` : "за период"} /><DrilldownCard title="Загрузка стола" value={busiestTable ? busiestTable.label : "нет данных"} meta={busiestTable ? `${Math.round(busiestTable.loadPercent)}%` : "за период"} /></div><div className="split-grid"><ChartWidget title="Популярные миксы" query={topMixesQuery} empty={mixRows.length === 0}>{mixRows.map((item) => <Bar key={item.mixId} label={item.label} value={item.ordersCount} max={topMixMax} suffix=" заказов" />)}</ChartWidget><ChartWidget title="Расход табака" query={tobaccoUsageQuery} empty={tobaccoRows.length === 0}>{tobaccoRows.map((item) => <Bar key={item.tobaccoId} label={item.label} value={Math.round(item.amount)} max={tobaccoMax} suffix=" г" />)}</ChartWidget><ChartWidget title="Эффективность кальянщиков" query={staffPerformanceQuery} empty={staffRows.length === 0}>{staffRows.map((item) => <Bar key={item.staffId} label={item.label} value={item.ordersServed} max={staffMax} suffix=" заказов" />)}</ChartWidget><ChartWidget title="Загрузка столов" query={tableLoadQuery} empty={tableRows.length === 0}>{tableRows.map((item) => <Bar key={item.tableId} label={item.label} value={Math.round(item.loadPercent)} max={100} suffix="%" />)}</ChartWidget></div></section>;
}

function ChartWidget({ title, query, empty, children }: { title: string; query: QueryState; empty: boolean; children: ReactNode }) {
  return <div className="chart-card"><div className="chart-head"><h3>{title}</h3>{query.isFetching && <span>обновляем</span>}</div>{query.isLoading ? <LoadingState label="Загружаем аналитику..." /> : query.isError ? <FormError error={query.error} message="Не удалось загрузить аналитику" /> : empty ? <EmptyState label="Нет данных для выбранного периода" /> : children}</div>;
}

function DrilldownCard({ title, value, meta }: { title: string; value: string; meta: string }) {
  return <article className="drilldown-card"><span>{title}</span><strong>{value}</strong><small>{meta}</small></article>;
}

function formatRub(value: number) {
  return `${Math.round(value).toLocaleString("ru-RU")} ₽`;
}

function Bar({ label, value, max, suffix = "" }: { label: string; value: number; max: number; suffix?: string }) {
  const width = `${Math.min(100, Math.round((value / Math.max(max, 1)) * 100))}%`;
  return <div className="bar-row"><div><span>{label}</span><strong>{value}{suffix}</strong></div><div className="chart-bar"><i style={{ width }} /></div></div>;
}

function NotificationsSection({ notifications, templates, users, token, onChanged }: { notifications: NotificationItem[]; templates: NotificationTemplate[]; users: UserProfile[]; token: string; onChanged: () => void | Promise<unknown> }) {
  const [userId, setUserId] = useState("");
  const [channel, setChannel] = useState("CRM");
  const [title, setTitle] = useState("");
  const [message, setMessage] = useState("");
  const [filter, setFilter] = useState<"ALL" | "UNREAD">("ALL");
  const [preferences, setPreferences] = useState<Omit<NotificationPreference, "userId">>({ crmEnabled: true, telegramEnabled: true, smsEnabled: true, emailEnabled: true, pushEnabled: true });
  const userIdValid = Boolean(userId);
  const markRead = useMutation({ mutationFn: (id: string) => patchJson(`/api/notifications/${id}/read`, {}, token), onSuccess: onChanged });
  const remove = useMutation({ mutationFn: (id: string) => deleteJson(`/api/notifications/${id}`, undefined, token), onSuccess: onChanged });
  const sendTest = useMutation({ mutationFn: () => postJson("/api/notifications/send", { userId, channel, title, message }, token), onSuccess: onChanged });
  const savePreferences = useMutation({ mutationFn: () => putJson(`/api/notifications/preferences/${userId}`, preferences, token), onSuccess: onChanged });
  const visible = filter === "UNREAD" ? notifications.filter((item) => !item.isRead) : notifications;
  return <section className="section"><div className="section-head"><h2>Уведомления</h2><span className="meta">center, read/unread, delete, preferences, templates</span></div><CrudToolbar title="Отправка и фильтр"><FormField label="Получатель"><UserSelect users={users} value={userId} onChange={setUserId} placeholder="Выберите получателя" required /></FormField><FormField label="Канал"><select value={channel} onChange={(e) => setChannel(e.target.value)}><option value="CRM">CRM</option><option value="TELEGRAM">Telegram</option><option value="EMAIL">Email</option><option value="SMS">SMS</option><option value="PUSH">Push</option></select></FormField><FormField label="Заголовок"><input value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Новая бронь" /></FormField><FormField label="Сообщение"><input value={message} onChange={(e) => setMessage(e.target.value)} placeholder="Клиент забронировал стол" /></FormField><FormField label="Фильтр"><select value={filter} onChange={(e) => setFilter(e.target.value as "ALL" | "UNREAD")}><option value="ALL">Все</option><option value="UNREAD">Непрочитанные</option></select></FormField><button className="mini-button" disabled={!userIdValid || !title.trim() || !message.trim() || sendTest.isPending} onClick={() => sendTest.mutate()}>Отправить</button></CrudToolbar>{users.length === 0 && <EmptyState label="Получатели не найдены" description="Нет активных клиентов или сотрудников для выбора." />}{mutationStatus(sendTest, "Уведомление отправлено")}<div className="preference-card">{(["crmEnabled", "telegramEnabled", "smsEnabled", "emailEnabled", "pushEnabled"] as const).map((key) => <label key={key}><input type="checkbox" checked={preferences[key]} onChange={(event) => setPreferences({ ...preferences, [key]: event.target.checked })} />{key.replace("Enabled", "")}</label>)}<button className="mini-button" disabled={!userIdValid} onClick={() => savePreferences.mutate()}>Сохранить preferences</button></div><NotificationTemplatesEditor templates={templates} token={token} onChanged={onChanged} />{visible.length === 0 ? <EmptyState label="Уведомлений нет" /> : <div className="queue cards">{visible.map((item) => <article className="notification-card" key={item.id}><div><strong>{item.title}</strong><p>{item.message}</p><span>{item.channel} · {item.isRead ? "read" : "unread"} · {timeLabel(item.createdAt)}</span></div><div className="row-actions"><button className={item.isRead ? "mini-button muted" : "mini-button"} disabled={item.isRead} onClick={() => markRead.mutate(item.id)}>{item.isRead ? "read" : "mark read"}</button><ActionButton danger pending={remove.isPending} pendingLabel="Удаляем..." confirm="Удалить уведомление?" onClick={() => remove.mutate(item.id)}>delete</ActionButton></div></article>)}</div>}</section>;
}

function NotificationTemplatesEditor({ templates, token, onChanged }: { templates: NotificationTemplate[]; token: string; onChanged: () => void | Promise<unknown> }) {
  const [code, setCode] = useState("");
  const [channel, setChannel] = useState("CRM");
  const [title, setTitle] = useState("");
  const [message, setMessage] = useState("");
  const create = useMutation({ mutationFn: () => postJson("/api/notifications/templates", { code, channel, title, message }, token), onSuccess: onChanged });
  return <div className="crud-card"><h3>Шаблоны уведомлений</h3><CrudToolbar title="Новый шаблон"><FormField label="Код"><input value={code} onChange={(e) => setCode(e.target.value)} placeholder="booking_created" /></FormField><FormField label="Канал"><select value={channel} onChange={(e) => setChannel(e.target.value)}><option value="CRM">CRM</option><option value="TELEGRAM">Telegram</option><option value="EMAIL">Email</option><option value="SMS">SMS</option><option value="PUSH">Push</option></select></FormField><FormField label="Заголовок"><input value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Заголовок" /></FormField><FormField label="Текст"><input value={message} onChange={(e) => setMessage(e.target.value)} placeholder="Текст" /></FormField><button disabled={!code.trim() || !title.trim() || !message.trim()} onClick={() => create.mutate()}>Создать шаблон</button></CrudToolbar>{templates.length === 0 ? <EmptyState label="Шаблонов нет" /> : templates.map((template) => <NotificationTemplateRow template={template} token={token} onChanged={onChanged} key={template.code} />)}</div>;
}

function NotificationTemplateRow({ template, token, onChanged }: { template: NotificationTemplate; token: string; onChanged: () => void | Promise<unknown> }) {
  const [channel, setChannel] = useState(template.channel);
  const [title, setTitle] = useState(template.title);
  const [message, setMessage] = useState(template.message);
  const validation = !title.trim() ? "Заголовок шаблона обязателен." : !message.trim() ? "Текст шаблона обязателен." : "";
  const update = useMutation({ mutationFn: () => putJson(`/api/notifications/templates/${encodeURIComponent(template.code)}`, { code: template.code, channel, title: title.trim(), message: message.trim() }, token), onSuccess: onChanged });
  const remove = useMutation({ mutationFn: () => deleteJson(`/api/notifications/templates/${encodeURIComponent(template.code)}`, undefined, token), onSuccess: onChanged });
  return <div className="crud-row-block"><div className="crud-row wide-row"><strong>{template.code}</strong><FormField label="Канал"><select value={channel} onChange={(e) => setChannel(e.target.value)}><option value="CRM">CRM</option><option value="TELEGRAM">Telegram</option><option value="EMAIL">Email</option><option value="SMS">SMS</option><option value="PUSH">Push</option></select></FormField><FormField label="Заголовок"><input value={title} onChange={(e) => setTitle(e.target.value)} /></FormField><FormField label="Текст"><input value={message} onChange={(e) => setMessage(e.target.value)} /></FormField><CrudRowActions><ActionButton disabled={Boolean(validation)} pending={update.isPending} pendingLabel="Сохраняем..." onClick={() => update.mutate()}>save</ActionButton><ActionButton danger pending={remove.isPending} pendingLabel="Удаляем..." confirm="Удалить шаблон?" onClick={() => remove.mutate()}>delete</ActionButton></CrudRowActions></div>{validationText(validation)}{mutationStatus(update, "Шаблон обновлен")}{mutationStatus(remove, "Шаблон удален")}</div>;
}

function ReviewsSection({ reviews, mixes, clients, token, onChanged }: { reviews: Review[]; mixes: Mix[]; clients: UserProfile[]; token: string; onChanged: () => void }) {
  const [clientId, setClientId] = useState("");
  const [mixId, setMixId] = useState("");
  const [rating, setRating] = useState(5);
  const [text, setText] = useState("");
  const clientIdValid = Boolean(clientId);
  const create = useMutation({ mutationFn: () => postJson("/api/reviews", { clientId, mixId: mixId || null, orderId: null, rating, text }, token), onSuccess: onChanged });
  return <section className="section"><div className="section-head"><h2>Отзывы</h2><span className="meta">create/edit/delete</span></div><CrudToolbar title="Новый отзыв"><FormField label="Клиент"><UserSelect users={clients} value={clientId} onChange={setClientId} placeholder="Выберите клиента" required /></FormField><FormField label="Микс"><select value={mixId} onChange={(e) => setMixId(e.target.value)}><option value="">Без привязки к миксу</option>{mixes.map((mix) => <option value={mix.id} key={mix.id}>{mix.name}</option>)}</select></FormField><FormField label="Оценка"><input type="number" min={1} max={5} value={rating} onChange={(e) => setRating(Number(e.target.value))} /></FormField><FormField label="Текст"><input value={text} onChange={(e) => setText(e.target.value)} placeholder="Текст отзыва" /></FormField><button disabled={!clientIdValid || rating < 1 || rating > 5 || !text.trim()} onClick={() => create.mutate()}>Создать</button></CrudToolbar>{clients.length === 0 && <EmptyState label="Клиенты не найдены" description="Создание отзыва требует выбрать существующего клиента." />}<div className="queue cards">{reviews.length === 0 ? <EmptyState label="Отзывов нет" /> : reviews.map((review) => <ReviewRow key={review.id} review={review} mixes={mixes} clients={clients} token={token} onChanged={onChanged} />)}</div></section>;
}

function ReviewRow({ review, mixes, clients, token, onChanged }: { review: Review; mixes: Mix[]; clients: UserProfile[]; token: string; onChanged: () => void }) {
  const [rating, setRating] = useState(review.rating);
  const [text, setText] = useState(review.text ?? "");
  const validation = rating < 1 || rating > 5 ? "Оценка должна быть от 1 до 5." : !text.trim() ? "Текст отзыва обязателен." : "";
  const update = useMutation({ mutationFn: () => patchJson(`/api/reviews/${review.id}`, { rating, text: text.trim() }, token), onSuccess: onChanged });
  const remove = useMutation({ mutationFn: () => deleteJson(`/api/reviews/${review.id}`, undefined, token), onSuccess: onChanged });
  return <article className="booking-row review-row"><div><strong>★ {rating} · {mixes.find((mix) => mix.id === review.mixId)?.name ?? shortId(review.mixId)}</strong><div className="meta">{clientName(review.clientId, clients)} · {timeLabel(review.createdAt)}</div></div><FormField label="Текст"><input value={text} onChange={(e) => setText(e.target.value)} /></FormField><FormField label="Оценка"><input type="number" min={1} max={5} value={rating} onChange={(e) => setRating(Number(e.target.value))} /></FormField><CrudRowActions><ActionButton disabled={Boolean(validation)} pending={update.isPending} pendingLabel="Сохраняем..." onClick={() => update.mutate()}>save</ActionButton><ActionButton danger pending={remove.isPending} pendingLabel="Удаляем..." confirm="Удалить отзыв?" onClick={() => remove.mutate()}>delete</ActionButton></CrudRowActions>{validationText(validation)}{mutationStatus(update, "Отзыв обновлен")}{mutationStatus(remove, "Отзыв удален")}</article>;
}

function PromoSection({ promocodes, token, onChanged }: { promocodes: Promocode[]; token: string; onChanged: () => void }) {
  const [code, setCode] = useState("");
  const [discountType, setDiscountType] = useState("PERCENT");
  const [discountValue, setDiscountValue] = useState(10);
  const [validFrom, setValidFrom] = useState(() => defaultDateInput());
  const [validTo, setValidTo] = useState(() => defaultDateInput(31));
  const [maxRedemptions, setMaxRedemptions] = useState(100);
  const [perClientLimit, setPerClientLimit] = useState(1);
  const validation = !code.trim() ? "Введите код." : discountValue <= 0 ? "Скидка должна быть больше 0." : discountType === "PERCENT" && discountValue > 100 ? "Процентная скидка не может быть больше 100." : !validFrom || !validTo || validTo < validFrom ? "Период действия некорректен." : perClientLimit <= 0 ? "Лимит на клиента должен быть больше 0." : maxRedemptions < 0 ? "Общий лимит не может быть отрицательным." : "";
  const create = useMutation({ mutationFn: () => postJson("/api/promocodes", { code: code.trim().toUpperCase(), discountType, discountValue, validFrom, validTo, maxRedemptions, perClientLimit }, token), onSuccess: async () => { setCode(""); setDiscountType("PERCENT"); setDiscountValue(10); setValidFrom(defaultDateInput()); setValidTo(defaultDateInput(31)); setMaxRedemptions(100); setPerClientLimit(1); await onChanged(); } });
  return <section className="section"><div className="section-head"><h2>Промокоды</h2><span className="meta">create/edit/deactivate + limits</span></div><CrudToolbar title="Новый промокод"><FormField label="Код"><input value={code} onChange={(e) => setCode(e.target.value.toUpperCase())} placeholder="HOOKAH20" /></FormField><FormField label="Тип скидки"><select value={discountType} onChange={(e) => setDiscountType(e.target.value)}><option value="PERCENT">PERCENT</option><option value="FIXED">FIXED</option></select></FormField><FormField label="Размер"><input type="number" min={0} value={discountValue} onChange={(e) => setDiscountValue(Number(e.target.value))} /></FormField><FormField label="С"><input type="date" value={validFrom} onChange={(e) => setValidFrom(e.target.value)} /></FormField><FormField label="По"><input type="date" value={validTo} onChange={(e) => setValidTo(e.target.value)} /></FormField><FormField label="Общий лимит"><input type="number" min={0} value={maxRedemptions} onChange={(e) => setMaxRedemptions(Number(e.target.value))} /></FormField><FormField label="Лимит клиента"><input type="number" min={1} value={perClientLimit} onChange={(e) => setPerClientLimit(Number(e.target.value))} /></FormField><button disabled={Boolean(validation) || create.isPending} onClick={() => create.mutate()}>Создать</button></CrudToolbar>{validationText(validation)}{mutationStatus(create, "Промокод создан")}{promocodes.length === 0 ? <EmptyState label="Промокодов нет" /> : <div className="queue cards">{promocodes.map((promo) => <PromoRow promo={promo} token={token} onChanged={onChanged} key={promo.code} />)}</div>}</section>;
}

function PromoRow({ promo, token, onChanged }: { promo: Promocode; token: string; onChanged: () => void }) {
  const [discountType, setDiscountType] = useState(promo.discountType);
  const [discountValue, setDiscountValue] = useState(promo.discountValue);
  const [validFrom, setValidFrom] = useState(promo.validFrom);
  const [validTo, setValidTo] = useState(promo.validTo);
  const [maxRedemptions, setMaxRedemptions] = useState(promo.maxRedemptions ?? 0);
  const [perClientLimit, setPerClientLimit] = useState(promo.perClientLimit);
  const [isActive, setIsActive] = useState(promo.isActive);
  const validation = discountValue <= 0 ? "Скидка должна быть больше 0." : discountType === "PERCENT" && discountValue > 100 ? "Процентная скидка не может быть больше 100." : !validFrom || !validTo || validTo < validFrom ? "Период действия некорректен." : perClientLimit <= 0 ? "Лимит на клиента должен быть больше 0." : maxRedemptions < 0 ? "Общий лимит не может быть отрицательным." : "";
  const update = useMutation({ mutationFn: () => patchJson(`/api/promocodes/${promo.code}`, { discountType, discountValue, validFrom, validTo, maxRedemptions: maxRedemptions || null, perClientLimit, isActive }, token), onSuccess: onChanged });
  const deactivate = useMutation({ mutationFn: () => patchJson(`/api/promocodes/${promo.code}/deactivate`, {}, token), onSuccess: onChanged });
  return <article className="booking-row promo-row"><div><strong>{promo.code}</strong><div className="meta">limit {promo.perClientLimit} · {promo.validFrom} - {promo.validTo}</div></div><FormField label="Тип"><select value={discountType} onChange={(e) => setDiscountType(e.target.value)}><option value="PERCENT">PERCENT</option><option value="FIXED">FIXED</option></select></FormField><FormField label="Размер"><input type="number" min={0} value={discountValue} onChange={(e) => setDiscountValue(Number(e.target.value))} /></FormField><FormField label="С"><input type="date" value={validFrom} onChange={(e) => setValidFrom(e.target.value)} /></FormField><FormField label="По"><input type="date" value={validTo} onChange={(e) => setValidTo(e.target.value)} /></FormField><FormField label="Общий лимит"><input type="number" min={0} value={maxRedemptions} onChange={(e) => setMaxRedemptions(Number(e.target.value))} /></FormField><FormField label="Лимит клиента"><input type="number" min={1} value={perClientLimit} onChange={(e) => setPerClientLimit(Number(e.target.value))} /></FormField><label className="check"><input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />active</label><span className={`status ${promo.isActive ? "ok" : "warn"}`}>{promo.isActive ? "ACTIVE" : "OFF"}</span><CrudRowActions><ActionButton disabled={Boolean(validation)} pending={update.isPending} pendingLabel="Сохраняем..." onClick={() => update.mutate()}>save</ActionButton><ActionButton danger disabled={!promo.isActive} pending={deactivate.isPending} pendingLabel="Отключаем..." confirm="Отключить промокод?" onClick={() => deactivate.mutate()}>Отключить</ActionButton></CrudRowActions>{validationText(validation)}{mutationStatus(update, "Промокод обновлен")}{mutationStatus(deactivate, "Промокод отключен")}</article>;
}

function MetricRow({ label, value }: { label: string; value: string }) {
  return <div className="metric-row"><span>{label}</span><strong>{value}</strong></div>;
}

function UserSelect({ users, value, onChange, placeholder, required = false }: { users: UserProfile[]; value: string; onChange: (value: string) => void; placeholder: string; required?: boolean }) {
  return <select value={value} onChange={(event) => onChange(event.target.value)} required={required}><option value="">{placeholder}</option>{users.map((user) => <option value={user.id} key={user.id}>{userLabel(user)}</option>)}</select>;
}

function userLabel(user: UserProfile) {
  return `${user.name} · ${user.phone}${user.role === "CLIENT" ? "" : ` · ${user.role}`}`;
}

function clientName(clientId: string, clients: UserProfile[]) {
  const client = clients.find((item) => item.id === clientId);
  return client ? userLabel(client) : shortId(clientId);
}

function uniqueProfiles(users: UserProfile[]) {
  const seen = new Set<string>();
  return users.filter((user) => {
    if (seen.has(user.id)) return false;
    seen.add(user.id);
    return true;
  });
}

function defaultDateInput(daysOffset = 0) {
  const date = new Date();
  date.setDate(date.getDate() + daysOffset);
  return date.toISOString().slice(0, 10);
}

function startOfWeekInput(value = defaultDateInput()) {
  const date = new Date(`${value}T00:00:00`);
  const mondayOffset = (date.getDay() + 6) % 7;
  date.setDate(date.getDate() - mondayOffset);
  return localDateInput(date);
}

function buildWeekDays(weekStart: string) {
  return Array.from({ length: 7 }, (_, index) => addDaysInput(weekStart, index));
}

function addDaysInput(value: string, days: number) {
  const date = new Date(`${value}T00:00:00`);
  date.setDate(date.getDate() + days);
  return localDateInput(date);
}

function localDateKey(value: string) {
  return localDateInput(new Date(value));
}

function localDateInput(date: Date) {
  const timezoneOffset = date.getTimezoneOffset() * 60_000;
  return new Date(date.getTime() - timezoneOffset).toISOString().slice(0, 10);
}

function weekdayLabel(value: string) {
  return new Date(`${value}T00:00:00`).toLocaleDateString("ru-RU", { weekday: "short", day: "2-digit", month: "2-digit" });
}

function zoneColor(color?: string | null) {
  if (!color || !/^#[0-9a-f]{6}$/i.test(color)) return "rgba(30, 118, 95, .08)";
  const red = Number.parseInt(color.slice(1, 3), 16);
  const green = Number.parseInt(color.slice(3, 5), 16);
  const blue = Number.parseInt(color.slice(5, 7), 16);
  return `rgba(${red}, ${green}, ${blue}, .12)`;
}

function defaultDateTimeInput(hour: number) {
  const date = new Date();
  const dayOffset = Math.floor(hour / 24);
  date.setDate(date.getDate() + dayOffset);
  date.setHours(hour % 24, 0, 0, 0);
  const timezoneOffset = date.getTimezoneOffset() * 60_000;
  return new Date(date.getTime() - timezoneOffset).toISOString().slice(0, 16);
}

function toDateTimeInput(value?: string | null) {
  if (!value) return "";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "";
  const timezoneOffset = date.getTimezoneOffset() * 60_000;
  return new Date(date.getTime() - timezoneOffset).toISOString().slice(0, 16);
}

function dateRangeIsValid(start: string, end: string) {
  return Boolean(start && end && new Date(start).getTime() < new Date(end).getTime());
}

function validationText(message?: string) {
  return <FieldError message={message} />;
}

function mutationStatus(mutation: { isPending?: boolean; isError?: boolean; isSuccess?: boolean; error?: unknown }, successMessage = "Сохранено") {
  return <MutationToast mutation={mutation} successMessage={successMessage} />;
}
