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
  const theme = resolveTheme(setting);
  document.documentElement.dataset.theme = theme;
  document.documentElement.dataset.themeSetting = setting;
  localStorage.setItem(storageKey, setting);
  return theme;
}
