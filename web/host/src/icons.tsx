import type { IconName } from "./catalog";

const paths: Record<IconName, string> = {
  home: "M4 11 12 4l8 7v9H4Z",
  clipboard: "M8 5h8v2H8Zm-2 2h12v13H6Z",
  chat: "M5 6h14v9H8l-3 3Z",
  flask: "M9 4h6M10 4v5L6 18h12l-4-9V4",
  activity: "M3 12h4l2-5 4 10 2-5h6",
  store: "M4 9h16l-1 3H5Zm1 3h14v8H5Z",
  devices: "M5 6h14v9H5Zm3 11h8",
  diagnostics: "M4 15l4-5 3 3 5-7 4 4",
  settings: "M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8Zm0-4v2m0 12v2M4 12H2m20 0h-2",
  folder: "M4 7h6l2 2h8v9H4Z",
  laptop: "M5 6h14v8H5Zm-2 10h18",
  server: "M5 4h14v6H5Zm0 10h14v6H5M8 7h.01M8 17h.01",
};

export function Icon({ name, size = 16 }: { name: IconName; size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.6">
      <path d={paths[name]} />
    </svg>
  );
}

export function WindowGlyph({ kind }: { kind: "min" | "max" | "close" }) {
  if (kind === "min") {
    return (
      <svg width="12" height="12" viewBox="0 0 12 12" aria-hidden="true">
        <rect x="1" y="5" width="10" height="2" rx="1" fill="currentColor" />
      </svg>
    );
  }
  if (kind === "max") {
    return (
      <svg width="12" height="12" viewBox="0 0 12 12" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.4">
        <rect x="1.5" y="1.5" width="9" height="9" rx="1" />
      </svg>
    );
  }
  return (
    <svg width="12" height="12" viewBox="0 0 12 12" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.4">
      <path d="M2 2l8 8M10 2 2 10" />
    </svg>
  );
}
