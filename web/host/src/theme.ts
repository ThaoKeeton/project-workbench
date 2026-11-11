export type ThemeSetting = "system" | "light" | "dark";

const storageKey = "workbench.theme";

export function readStoredSetting(): ThemeSetting {
  const value = localStorage.getItem(storageKey);
  return value === "light" || value === "dark" || value === "system" ? value : "system";
}

export function resolveTheme(setting: ThemeSetting): "light" | "dark" {
  if (setting === "light" || setting === "dark") return setting;
  return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

export function applyTheme(setting: ThemeSetting): "light" | "dark" {
  const stored = setting === "light" || setting === "dark" ? setting : "system";
  const theme = resolveTheme(stored);
  document.documentElement.dataset.theme = theme;
  document.documentElement.dataset.themeSetting = stored;
  // Only the three allowlisted literals are written. Anything else becomes "system".
  if (stored === "light") localStorage.setItem(storageKey, "light");
  else if (stored === "dark") localStorage.setItem(storageKey, "dark");
  else localStorage.setItem(storageKey, "system");
  return theme;
}
