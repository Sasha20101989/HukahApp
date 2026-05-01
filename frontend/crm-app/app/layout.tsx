import type { Metadata, Viewport } from "next";
import "./globals.css";
import { Providers } from "./providers";

export const metadata: Metadata = {
  title: "Hookah CRM",
  description: "Tablet-first CRM workspace for hookah venue staff",
  manifest: "/manifest.webmanifest"
};

export const viewport: Viewport = {
  themeColor: "#183f35"
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="ru">
      <body><Providers>{children}</Providers></body>
    </html>
  );
}
