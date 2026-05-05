"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Building2, Flame, LogOut, ShieldCheck } from "lucide-react";
import type { ReactNode } from "react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { configureApiAuth, createTenant, getJson, getTenantSettings, getTenants, hasPermission, logoutAuth, updateTenant, updateTenantSettings, type Tenant, type TenantSettings, type UserProfile } from "../../../lib/api";
import { hydrateCrmSession, useCrmStore } from "../../../lib/store";
import { ActionButton, CrudRowActions, CrudToolbar, EmptyState, FieldError, FormError, FormField, LoadingState, MutationToast } from "../../../lib/ui";

const defaultTimezone = "Europe/Moscow";
const defaultCurrency = "RUB";

export default function TenantAdminPage() {
  const queryClient = useQueryClient();
  const { hydrated, session, setAuth, setProfile, logout } = useCrmStore();
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

  const profile = useQuery({
    queryKey: ["tenant-admin", "me", token],
    enabled: Boolean(token),
    queryFn: () => authorized((accessToken) => getJson<UserProfile>("/api/users/me", accessToken)),
    retry: false
  });

  useEffect(() => {
    if (profile.data) setProfile(profile.data);
  }, [profile.data, setProfile]);

  const canManageTenants = hasPermission(role, "tenants.manage");
  const tenants = useQuery({
    queryKey: ["tenants"],
    enabled: Boolean(token && role && canManageTenants),
    queryFn: () => authorized((accessToken) => getTenants(accessToken))
  });

  const handleLogout = useCallback(async () => {
    if (session.refreshToken) {
      try { await logoutAuth(session.refreshToken); } catch { /* local logout still wins */ }
    }
    logout();
  }, [logout, session.refreshToken]);

  if (!hydrated) return <TenantShell><LoadingState label="Загружаем сессию..." /></TenantShell>;
  if (!token || profile.isError) return <TenantShell><FormError error={profile.error} message={profile.error instanceof Error ? profile.error.message : "Нужна авторизация OWNER."} /><a className="primary" href="/login">Войти</a></TenantShell>;
  if (!role) return <TenantShell><LoadingState label="Проверяем профиль..." /></TenantShell>;
  if (!canManageTenants) return <TenantShell><FormError message="Недостаточно прав: требуется tenants.manage." /><a className="primary" href="/">Вернуться в CRM</a></TenantShell>;

  return (
    <main className="shell">
      <aside className="sidebar">
        <div className="brand"><span className="brand-mark"><Flame size={20} /></span><span>Hookah CRM</span></div>
        <div className="role-card"><span><ShieldCheck size={15} /> Admin console</span><strong>{session.profile?.name}</strong><small>{role}</small><button className="ghost" onClick={handleLogout}><LogOut size={15} />Выйти</button></div>
        <nav className="nav" aria-label="Admin sections"><a className="admin-link" href="/"><Flame size={18} />CRM</a><a className="admin-link" href="/admin/tenants"><Building2 size={18} />Тенанты</a><a className="admin-link" href="/admin/roles">Роли</a></nav>
      </aside>

      <section className="main">
        <header className="topbar">
          <div className="title"><h1>Tenant Admin</h1><p>Создание tenant, статус активности и базовые настройки SaaS-изоляции.</p></div>
        </header>

        <section className="section">
          <div className="section-head"><h2>Тенанты</h2><span className="meta">permission: tenants.manage</span></div>
          <TenantCreateForm token={token} onChanged={() => queryClient.invalidateQueries({ queryKey: ["tenants"] })} />
          {tenants.isLoading && <LoadingState label="Загружаем tenants..." />}
          {tenants.isError && <FormError error={tenants.error} />}
          {!tenants.isLoading && !tenants.isError && (tenants.data?.length ? <div className="queue cards">{tenants.data.map((tenant) => <TenantRow key={tenant.id} tenant={tenant} token={token} onChanged={() => Promise.all([queryClient.invalidateQueries({ queryKey: ["tenants"] }), queryClient.invalidateQueries({ queryKey: ["tenant-settings", tenant.id] })])} />)}</div> : <EmptyState label="Тенанты не созданы" description="Создайте первый tenant для новой организации или сети филиалов." />)}
        </section>
      </section>
    </main>
  );
}

function TenantShell({ children }: { children: ReactNode }) {
  return <main className="auth-shell"><section className="auth-card"><div className="brand"><span className="brand-mark"><Building2 size={20} /></span><span>Tenant Admin</span></div>{children}</section></main>;
}

function TenantCreateForm({ token, onChanged }: { token: string; onChanged: () => void | Promise<unknown> }) {
  const [name, setName] = useState("");
  const [slug, setSlug] = useState("");
  const [timezone, setTimezone] = useState(defaultTimezone);
  const [currency, setCurrency] = useState(defaultCurrency);
  const [requireDeposit, setRequireDeposit] = useState(true);
  const validation = tenantValidation(name, slug, timezone, currency);
  const create = useMutation({
    mutationFn: async () => {
      const tenant = await createTenant({ name: name.trim(), slug: slug.trim() }, token);
      await updateTenantSettings(tenant.id, { tenantId: tenant.id, defaultTimezone: timezone.trim(), defaultCurrency: currency.trim().toUpperCase(), requireDeposit }, token);
      return tenant;
    },
    onSuccess: async () => {
      setName("");
      setSlug("");
      setTimezone(defaultTimezone);
      setCurrency(defaultCurrency);
      setRequireDeposit(true);
      await onChanged();
    }
  });

  return (
    <CrudToolbar title="Новый tenant">
      <FormField label="Название"><input value={name} onChange={(event) => setName(event.target.value)} placeholder="Hookah Place" /></FormField>
      <FormField label="Slug"><input value={slug} onChange={(event) => setSlug(normalizeSlug(event.target.value))} placeholder="hookah-place" /></FormField>
      <FormField label="Timezone"><input value={timezone} onChange={(event) => setTimezone(event.target.value)} placeholder={defaultTimezone} /></FormField>
      <FormField label="Currency"><input value={currency} onChange={(event) => setCurrency(event.target.value.toUpperCase())} placeholder={defaultCurrency} /></FormField>
      <label className="check"><input type="checkbox" checked={requireDeposit} onChange={(event) => setRequireDeposit(event.target.checked)} />Депозит обязателен</label>
      <button disabled={Boolean(validation) || create.isPending} onClick={() => create.mutate()}>{create.isPending ? "Создаем..." : "Создать"}</button>
      <FieldError message={validation} />
      <MutationToast mutation={create} successMessage="Tenant создан" />
    </CrudToolbar>
  );
}

function TenantRow({ tenant, token, onChanged }: { tenant: Tenant; token: string; onChanged: () => void | Promise<unknown> }) {
  const [name, setName] = useState(tenant.name);
  const [slug, setSlug] = useState(tenant.slug);
  const [isActive, setIsActive] = useState(tenant.isActive);
  const settings = useQuery({ queryKey: ["tenant-settings", tenant.id], queryFn: () => getTenantSettings(tenant.id, token) });
  const initialSettings = useMemo(() => settings.data ?? { tenantId: tenant.id, defaultTimezone, defaultCurrency, requireDeposit: true }, [settings.data, tenant.id]);
  const [settingsDraft, setSettingsDraft] = useState<TenantSettings>(initialSettings);

  useEffect(() => {
    setName(tenant.name);
    setSlug(tenant.slug);
    setIsActive(tenant.isActive);
  }, [tenant]);

  useEffect(() => {
    setSettingsDraft(initialSettings);
  }, [initialSettings]);

  const validation = tenantValidation(name, slug, settingsDraft.defaultTimezone, settingsDraft.defaultCurrency);
  const save = useMutation({
    mutationFn: async () => {
      const updatedTenant = await updateTenant(tenant.id, { name: name.trim(), slug: slug.trim(), isActive }, token);
      await updateTenantSettings(tenant.id, {
        tenantId: tenant.id,
        defaultTimezone: settingsDraft.defaultTimezone.trim(),
        defaultCurrency: settingsDraft.defaultCurrency.trim().toUpperCase(),
        requireDeposit: settingsDraft.requireDeposit
      }, token);
      return updatedTenant;
    },
    onSuccess: onChanged
  });

  return (
    <article className="booking-row booking-row-rich">
      <div>
        <strong>{tenant.name}</strong>
        <div className="meta">{tenant.slug} · created {new Date(tenant.createdAt).toLocaleDateString("ru-RU")}</div>
        {!tenant.isActive && <span className="status danger">inactive</span>}
      </div>
      <div className="crud-row wide-row">
        <FormField label="Название"><input value={name} onChange={(event) => setName(event.target.value)} /></FormField>
        <FormField label="Slug"><input value={slug} onChange={(event) => setSlug(normalizeSlug(event.target.value))} /></FormField>
        <FormField label="Статус"><select value={isActive ? "active" : "inactive"} onChange={(event) => setIsActive(event.target.value === "active")}><option value="active">active</option><option value="inactive">inactive</option></select></FormField>
        <FormField label="Timezone"><input value={settingsDraft.defaultTimezone} onChange={(event) => setSettingsDraft((draft) => ({ ...draft, defaultTimezone: event.target.value }))} /></FormField>
        <FormField label="Currency"><input value={settingsDraft.defaultCurrency} onChange={(event) => setSettingsDraft((draft) => ({ ...draft, defaultCurrency: event.target.value.toUpperCase() }))} /></FormField>
        <label className="check"><input type="checkbox" checked={settingsDraft.requireDeposit} onChange={(event) => setSettingsDraft((draft) => ({ ...draft, requireDeposit: event.target.checked }))} />Депозит</label>
        <CrudRowActions><ActionButton disabled={Boolean(validation) || settings.isLoading} pending={save.isPending} pendingLabel="Сохраняем..." onClick={() => save.mutate()}>save</ActionButton></CrudRowActions>
      </div>
      {settings.isLoading && <LoadingState label="Загружаем settings..." />}
      {settings.isError && <FormError error={settings.error} />}
      <FieldError message={validation} />
      <MutationToast mutation={save} successMessage="Tenant обновлен" />
    </article>
  );
}

function tenantValidation(name: string, slug: string, timezone: string, currency: string) {
  if (!name.trim()) return "Введите название tenant.";
  if (!/^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$/.test(slug.trim())) return "Slug: 3-64 символа, lowercase latin, цифры и дефисы, без дефиса по краям.";
  if (!timezone.trim()) return "Введите timezone.";
  if (!/^[A-Z]{3}$/.test(currency.trim().toUpperCase())) return "Currency должна быть ISO-кодом из 3 букв.";
  return "";
}

function normalizeSlug(value: string) {
  return value.toLowerCase().replace(/[^a-z0-9-]/g, "-").replace(/-+/g, "-").slice(0, 64);
}
