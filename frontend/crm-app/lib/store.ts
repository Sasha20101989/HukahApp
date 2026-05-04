import { create } from "zustand";
import type { AuthResponse, RoleCode, UserProfile } from "./api";

export type CrmSection = "floor" | "orders" | "bookings" | "inventory" | "mixology" | "staff" | "analytics" | "notifications" | "reviews" | "promo";

export type CrmSession = {
  userId?: string;
  accessToken?: string;
  refreshToken?: string;
  profile?: UserProfile;
};

export type CrmState = {
  branchId: string;
  section: CrmSection;
  search: string;
  session: CrmSession;
  hydrated: boolean;
  setHydrated: (hydrated: boolean) => void;
  setBranchId: (branchId: string) => void;
  setSection: (section: CrmSection) => void;
  setSearch: (search: string) => void;
  setAuth: (auth: AuthResponse) => void;
  setProfile: (profile: UserProfile) => void;
  logout: () => void;
};

const storageKey = "hookah.crm.session";
const authCookie = "hookah_crm_access_token";

export const useCrmStore = create<CrmState>((set, get) => ({
  branchId: "",
  section: "floor",
  search: "",
  session: {},
  hydrated: false,
  setHydrated: (hydrated) => set({ hydrated }),
  setBranchId: (branchId) => set({ branchId }),
  setSection: (section) => set({ section }),
  setSearch: (search) => set({ search }),
  setAuth: (auth) => {
    const session = { ...get().session, ...auth };
    persistSession(session);
    set({ session });
  },
  setProfile: (profile) => {
    const session = { ...get().session, profile };
    persistSession(session);
    set({ session, branchId: profile.branchId ?? get().branchId });
  },
  logout: () => {
    if (typeof window !== "undefined") {
      window.localStorage.removeItem(storageKey);
      document.cookie = `${authCookie}=; path=/; max-age=0; SameSite=Lax`;
    }
    set({ session: {}, section: "floor" });
  }
}));

export function hydrateCrmSession() {
  if (typeof window === "undefined") return;
  const raw = window.localStorage.getItem(storageKey);
  if (!raw) {
    useCrmStore.getState().setHydrated(true);
    return;
  }
  try {
    const session = JSON.parse(raw) as CrmSession;
    useCrmStore.setState({ session, hydrated: true, branchId: session.profile?.branchId ?? useCrmStore.getState().branchId });
  } catch {
    window.localStorage.removeItem(storageKey);
    useCrmStore.getState().setHydrated(true);
  }
}

export function currentRole(): RoleCode | undefined {
  return useCrmStore.getState().session.profile?.role;
}

function persistSession(session: CrmSession) {
  if (typeof window !== "undefined") {
    window.localStorage.setItem(storageKey, JSON.stringify(session));
    if (session.accessToken) document.cookie = `${authCookie}=1; path=/; max-age=604800; SameSite=Lax`;
  }
}
