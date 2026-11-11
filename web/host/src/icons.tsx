type IconProps = { name: "home" | "market" | "projects" | "devices" | "diagnostics" | "settings" | "search" };

export function Icon({ name }: IconProps) {
  const path = {
    home: "M4 10.5 12 4l8 6.5V20H4Z",
    market: "M4 7h16v3H4Zm1 3h14v10H5Z",
    projects: "M4 5h7v7H4Zm9 0h7v7h-7ZM4 14h7v5H4Zm9 0h7v5h-7Z",
    devices: "M7 5h10v10H7Zm-2 12h14",
    diagnostics: "M4 16l4-5 3 3 5-7 4 4",
    settings: "M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8Zm0-4v2m0 12v2M4 12H2m20 0h-2M6 6l1.5 1.5M16.5 16.5 18 18M18 6l-1.5 1.5M7.5 16.5 6 18",
    search: "M10 5a5 5 0 1 0 0 10 5 5 0 0 0 0-10Zm6 10 3 3",
  }[name];
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.6">
      <path d={path} />
    </svg>
  );
}
