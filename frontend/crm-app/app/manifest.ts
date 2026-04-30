import type { MetadataRoute } from "next";

export default function manifest(): MetadataRoute.Manifest {
  return {
    name: "Hookah CRM",
    short_name: "Hookah CRM",
    description: "Tablet-first CRM workspace for hookah venue staff",
    start_url: "/",
    display: "standalone",
    background_color: "#eef4f1",
    theme_color: "#1f7a66",
    icons: [
      {
        src: "/icon.svg",
        sizes: "any",
        type: "image/svg+xml"
      }
    ]
  };
}
