import { create } from "zustand";

export type ClientStep = "profile" | "time" | "mix" | "payment" | "history";

export type ClientSession = {
  userId?: string;
  accessToken?: string;
  refreshToken?: string;
  name: string;
  phone: string;
  email: string;
  password: string;
};

export type BookingDraft = {
  branchId: string;
  tableId?: string;
  holdId?: string;
  mixId: string;
  bowlId: string;
  hookahId: string;
  paymentId?: string;
  paymentUrl?: string;
  paymentReturnUrl?: string;
  bookingId?: string;
  date: string;
  time: string;
  guests: number;
  comment: string;
  promocode: string;
};

export type ClientStore = {
  step: ClientStep;
  session: ClientSession;
  draft: BookingDraft;
  hydrated: boolean;
  setHydrated: (hydrated: boolean) => void;
  setStep: (step: ClientStep) => void;
  setSession: (session: Partial<ClientSession>) => void;
  setDraft: (draft: Partial<BookingDraft>) => void;
  logout: () => void;
};

const storageKey = "hookah.client.session";
const authCookie = "hookah_client_access_token";

export const useClientStore = create<ClientStore>((set, get) => ({
  step: "profile",
  session: { name: "", phone: "", email: "", password: "" },
  draft: {
    branchId: "",
    mixId: "",
    bowlId: "",
    hookahId: "",
    paymentId: undefined,
    paymentUrl: undefined,
    paymentReturnUrl: undefined,
    bookingId: undefined,
    date: new Date().toISOString().slice(0, 10),
    time: "20:00",
    guests: 4,
    comment: "",
    promocode: ""
  },
  hydrated: false,
  setHydrated: (hydrated) => set({ hydrated }),
  setStep: (step) => set({ step }),
  setSession: (session) => {
    const next = { ...get().session, ...session };
    persistSession(next);
    set({ session: next });
  },
  setDraft: (draft) => set((state) => ({ draft: { ...state.draft, ...draft } })),
  logout: () => {
    if (typeof window !== "undefined") {
      window.localStorage.removeItem(storageKey);
      document.cookie = `${authCookie}=; path=/; max-age=0; SameSite=Lax`;
    }
    set({ session: { name: "", phone: "", email: "", password: "" }, step: "profile" });
  }
}));

export function hydrateClientSession() {
  if (typeof window === "undefined") return;
  const raw = window.localStorage.getItem(storageKey);
  if (!raw) {
    useClientStore.getState().setHydrated(true);
    return;
  }
  try {
    const session = { ...JSON.parse(raw), password: "" } as ClientSession;
    useClientStore.setState({ session, hydrated: true, step: session.accessToken ? "time" : "profile" });
  } catch {
    window.localStorage.removeItem(storageKey);
    useClientStore.getState().setHydrated(true);
  }
}

function persistSession(session: ClientSession) {
  if (typeof window !== "undefined") {
    const { password: _password, ...persistedSession } = session;
    window.localStorage.setItem(storageKey, JSON.stringify(persistedSession));
    if (session.accessToken) document.cookie = `${authCookie}=1; path=/; max-age=604800; SameSite=Lax`;
  }
}
