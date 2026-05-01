import { create } from "zustand";

export type ClientStep = "profile" | "time" | "mix" | "payment" | "history";

export type ClientSession = {
  userId?: string;
  accessToken?: string;
  refreshToken?: string;
  name: string;
  phone: string;
  email: string;
};

export type BookingDraft = {
  branchId: string;
  tableId?: string;
  holdId?: string;
  mixId: string;
  bowlId: string;
  hookahId: string;
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
  setStep: (step: ClientStep) => void;
  setSession: (session: Partial<ClientSession>) => void;
  setDraft: (draft: Partial<BookingDraft>) => void;
};

export const useClientStore = create<ClientStore>((set) => ({
  step: "profile",
  session: { name: "", phone: "", email: "" },
  draft: {
    branchId: "10000000-0000-0000-0000-000000000001",
    mixId: "70000000-0000-0000-0000-000000000001",
    bowlId: "50000000-0000-0000-0000-000000000001",
    hookahId: "40000000-0000-0000-0000-000000000001",
    date: "2026-05-01",
    time: "20:00",
    guests: 4,
    comment: "",
    promocode: ""
  },
  setStep: (step) => set({ step }),
  setSession: (session) => set((state) => ({ session: { ...state.session, ...session } })),
  setDraft: (draft) => set((state) => ({ draft: { ...state.draft, ...draft } }))
}));
