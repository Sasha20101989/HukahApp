import { create } from "zustand";

export type CrmRole = "OWNER" | "MANAGER" | "HOOKAH_MASTER" | "WAITER";
export type CrmSection = "floor" | "orders" | "bookings" | "inventory" | "mixology" | "staff";

export type CrmState = {
  branchId: string;
  role: CrmRole;
  section: CrmSection;
  search: string;
  setBranchId: (branchId: string) => void;
  setRole: (role: CrmRole) => void;
  setSection: (section: CrmSection) => void;
  setSearch: (search: string) => void;
};

export const useCrmStore = create<CrmState>((set) => ({
  branchId: "10000000-0000-0000-0000-000000000001",
  role: "MANAGER",
  section: "floor",
  search: "",
  setBranchId: (branchId) => set({ branchId }),
  setRole: (role) => set({ role }),
  setSection: (section) => set({ section }),
  setSearch: (search) => set({ search })
}));
