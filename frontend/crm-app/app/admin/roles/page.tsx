"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Flame, LogOut, ShieldCheck, UserCog } from "lucide-react";
import type { ReactNode } from "react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { configureApiAuth, createRole, deleteRole, getJson, getPermissions, getRoles, logoutAuth, updateRole, updateRolePermissions, type PermissionDefinition, type TenantRole, type UserProfile } from "../../../lib/api";
import { hydrateCrmSession, useCrmStore } from "../../../lib/store";
import { ActionButton, CrudRowActions, CrudToolbar, EmptyState, FieldError, FormError, FormField, LoadingState, MutationToast } from "../../../lib/ui";

export default function RoleAdminPage() {
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
    queryKey: ["role-admin", "me", token],
    enabled: Boolean(token),
    queryFn: () => authorized((accessToken) => getJson<UserProfile>("/api/users/me", accessToken)),
    retry: false
  });
  useEffect(() => { if (profile.data) setProfile(profile.data); }, [profile.data, setProfile]);

  const canManageRoles = role === "OWNER";
  const roles = useQuery({ queryKey: ["roles"], enabled: Boolean(token && canManageRoles), queryFn: () => authorized((accessToken) => getRoles(accessToken)) });
  const permissions = useQuery({ queryKey: ["permissions"], enabled: Boolean(token && canManageRoles), queryFn: () => authorized((accessToken) => getPermissions(accessToken)) });

  const handleLogout = useCallback(async () => {
    if (session.refreshToken) {
      try { await logoutAuth(session.refreshToken); } catch { /* local logout still wins */ }
    }
    logout();
  }, [logout, session.refreshToken]);

  if (!hydrated) return <RoleShell><LoadingState label="Загружаем сессию..." /></RoleShell>;
  if (!token || profile.isError) return <RoleShell><FormError error={profile.error} message={profile.error instanceof Error ? profile.error.message : "Нужна авторизация OWNER."} /><a className="primary" href="/login">Войти</a></RoleShell>;
  if (!role) return <RoleShell><LoadingState label="Проверяем профиль..." /></RoleShell>;
  if (!canManageRoles) return <RoleShell><FormError message="Редактор ролей доступен только OWNER." /><a className="primary" href="/">Вернуться в CRM</a></RoleShell>;

  return (
    <main className="shell">
      <aside className="sidebar">
        <div className="brand"><span className="brand-mark"><Flame size={20} /></span><span>Hookah CRM</span></div>
        <div className="role-card"><span><ShieldCheck size={15} /> Admin console</span><strong>{session.profile?.name}</strong><small>{role}</small><button className="ghost" onClick={handleLogout}><LogOut size={15} />Выйти</button></div>
        <nav className="nav" aria-label="Admin sections"><a className="admin-link" href="/"><Flame size={18} />CRM</a><a className="admin-link" href="/admin/tenants">Тенанты</a><a className="admin-link" href="/admin/roles"><UserCog size={18} />Роли</a></nav>
      </aside>
      <section className="main">
        <header className="topbar"><div className="title"><h1>Role Editor</h1><p>Создание tenant-scoped ролей и назначение permissions. OWNER остается системной wildcard-ролью.</p></div></header>
        <section className="section">
          <div className="section-head"><h2>Роли</h2><span className="meta">owner-only UI</span></div>
          <RoleCreateForm token={token} permissions={permissions.data ?? []} onChanged={() => queryClient.invalidateQueries({ queryKey: ["roles"] })} />
          {(roles.isLoading || permissions.isLoading) && <LoadingState label="Загружаем роли и permissions..." />}
          {(roles.isError || permissions.isError) && <FormError error={roles.error ?? permissions.error} />}
          {!roles.isLoading && !permissions.isLoading && !roles.isError && !permissions.isError && (roles.data?.length ? <div className="queue cards">{roles.data.map((tenantRole) => <RoleRow key={tenantRole.id} role={tenantRole} permissions={permissions.data ?? []} token={token} onChanged={() => queryClient.invalidateQueries({ queryKey: ["roles"] })} />)}</div> : <EmptyState label="Роли не созданы" description="Создайте кастомную роль для персонала tenant." />)}
        </section>
      </section>
    </main>
  );
}

function RoleShell({ children }: { children: ReactNode }) {
  return <main className="auth-shell"><section className="auth-card"><div className="brand"><span className="brand-mark"><UserCog size={20} /></span><span>Role Editor</span></div>{children}</section></main>;
}

function RoleCreateForm({ token, permissions, onChanged }: { token: string; permissions: PermissionDefinition[]; onChanged: () => void | Promise<unknown> }) {
  const [name, setName] = useState("");
  const [code, setCode] = useState("");
  const [selected, setSelected] = useState<string[]>([]);
  const validation = roleValidation(name, code);
  const create = useMutation({
    mutationFn: async () => {
      const created = await createRole({ name: name.trim(), code: normalizeRoleCode(code) }, token);
      if (selected.length > 0) return updateRolePermissions(created.id, selected, token);
      return created;
    },
    onSuccess: async () => {
      setName("");
      setCode("");
      setSelected([]);
      await onChanged();
    }
  });

  return <CrudToolbar title="Новая роль"><FormField label="Название"><input value={name} onChange={(event) => setName(event.target.value)} placeholder="Старший кальянщик" /></FormField><FormField label="Code"><input value={code} onChange={(event) => setCode(normalizeRoleCode(event.target.value))} placeholder="SENIOR_HOOKAH_MASTER" /></FormField><PermissionPicker permissions={permissions} selected={selected} onChange={setSelected} /><button disabled={Boolean(validation) || create.isPending} onClick={() => create.mutate()}>{create.isPending ? "Создаем..." : "Создать"}</button><FieldError message={validation} /><MutationToast mutation={create} successMessage="Роль создана" /></CrudToolbar>;
}

function RoleRow({ role, permissions, token, onChanged }: { role: TenantRole; permissions: PermissionDefinition[]; token: string; onChanged: () => void | Promise<unknown> }) {
  const [name, setName] = useState(role.name);
  const [isActive, setIsActive] = useState(role.isActive);
  const [selected, setSelected] = useState<string[]>(role.permissions.includes("*") ? [] : role.permissions);
  useEffect(() => { setName(role.name); setIsActive(role.isActive); setSelected(role.permissions.includes("*") ? [] : role.permissions); }, [role]);
  const validation = !name.trim() ? "Введите название роли." : "";
  const save = useMutation({
    mutationFn: async () => {
      const updated = await updateRole(role.id, { name: name.trim(), isActive }, token);
      if (role.isSystem) return updated;
      return updateRolePermissions(role.id, selected, token);
    },
    onSuccess: onChanged
  });
  const remove = useMutation({ mutationFn: () => deleteRole(role.id, token), onSuccess: onChanged });
  const visiblePermissions = role.permissions.includes("*") ? ["*"] : selected;

  return <article className="booking-row booking-row-rich"><div><strong>{role.name}</strong><div className="meta">{role.code} · {role.isSystem ? "system" : "custom"}</div><div className="pill-row">{visiblePermissions.length ? visiblePermissions.map((permission) => <span key={permission}>{permission}</span>) : <span>no permissions</span>}</div></div><div className="crud-row wide-row"><FormField label="Название"><input value={name} onChange={(event) => setName(event.target.value)} disabled={role.isSystem} /></FormField><FormField label="Code"><input value={role.code} disabled /></FormField><FormField label="Статус"><select value={isActive ? "active" : "inactive"} onChange={(event) => setIsActive(event.target.value === "active")} disabled={role.isSystem}><option value="active">active</option><option value="inactive">inactive</option></select></FormField>{!role.isSystem && <PermissionPicker permissions={permissions} selected={selected} onChange={setSelected} />}<CrudRowActions><ActionButton disabled={Boolean(validation)} pending={save.isPending} pendingLabel="Сохраняем..." onClick={() => save.mutate()}>save</ActionButton>{!role.isSystem && <ActionButton danger pending={remove.isPending} pendingLabel="Удаляем..." confirm="Удалить роль?" onClick={() => remove.mutate()}>delete</ActionButton>}</CrudRowActions></div><FieldError message={validation} /><MutationToast mutation={save} successMessage="Роль обновлена" /><MutationToast mutation={remove} successMessage="Роль удалена" /></article>;
}

function PermissionPicker({ permissions, selected, onChange }: { permissions: PermissionDefinition[]; selected: string[]; onChange: (permissions: string[]) => void }) {
  const selectedSet = useMemo(() => new Set(selected), [selected]);
  const toggle = (code: string) => onChange(selectedSet.has(code) ? selected.filter((permission) => permission !== code) : [...selected, code].sort());
  return <div className="permission-picker">{permissions.map((permission) => <label className="check" title={permission.description} key={permission.code}><input type="checkbox" checked={selectedSet.has(permission.code)} onChange={() => toggle(permission.code)} />{permission.code}</label>)}</div>;
}

function roleValidation(name: string, code: string) {
  if (!name.trim()) return "Введите название роли.";
  if (!/^[A-Z0-9_]{2,80}$/.test(normalizeRoleCode(code))) return "Code: 2-80 символов, uppercase latin, цифры и underscore.";
  return "";
}

function normalizeRoleCode(value: string) {
  return value.toUpperCase().replace(/[^A-Z0-9_]/g, "_").replace(/_+/g, "_").slice(0, 80);
}
