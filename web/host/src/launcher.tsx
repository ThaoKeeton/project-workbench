import { useEffect, useMemo, useState, type ReactNode } from "react";
import { useNavigate } from "react-router-dom";
import { devices, pinnedPlugins, recentPlugins, todayItems, type DeviceId, type IconName } from "./catalog";
import { Icon } from "./icons";
import { Keycap } from "./shell";

export type PaletteEntry = {
  id: string;
  group: "插件" | "命令" | "设备" | "项目";
  label: string;
  detail?: string;
  run: () => void;
};

export function HomePage({ onOpenPalette, palette }: { onOpenPalette: () => void; palette?: ReactNode }) {
  return (
    <div className="launcher">
      {palette ?? (
        <button type="button" className="command-bar" onClick={onOpenPalette}>
          <Icon name="home" size={18} />
          <span>搜索插件、项目、设备或命令…</span>
          <span className="shortcut">Ctrl K</span>
        </button>
      )}
      {palette ? null : (
        <>
          <section className="column">
            <h2 className="section">今日</h2>
            <ul className="today">
              {todayItems.map((item) => (
                <li key={item.time}>
                  <span className="time">{item.time}</span>
                  <span className="source">{item.source}</span>
                  <span>{item.title}</span>
                </li>
              ))}
            </ul>
          </section>
          <section className="column">
            <h2 className="section">最近打开</h2>
            <div className="tiles">
              {recentPlugins.map((plugin) => (
                <PluginTile key={plugin.id} id={plugin.id} name={plugin.name} hint={plugin.hint} icon={plugin.icon} />
              ))}
            </div>
          </section>
        </>
      )}
    </div>
  );
}

function PluginTile({ id, name, hint, icon }: { id: string; name: string; hint: string; icon: IconName }) {
  const navigate = useNavigate();
  return (
    <button type="button" className="tile" onClick={() => { void navigate(`/plugin/${id}`); }}>
      <Icon name={icon} size={28} />
      <strong>{name}</strong>
      <span>{hint}</span>
    </button>
  );
}

export function CommandPalette({
  projects,
  activeProjectId,
  onClose,
  onOpenPlugin,
  onPickDevice,
  onSwitchProject,
}: {
  projects: Array<{ id: string; name: string }>;
  activeProjectId: string;
  onClose: () => void;
  onOpenPlugin: (id: string) => void;
  onPickDevice: (id: DeviceId) => void;
  onSwitchProject: (id: string) => void;
}) {
  const navigate = useNavigate();
  const [query, setQuery] = useState("");
  const [cursor, setCursor] = useState(0);
  const entries = useMemo<PaletteEntry[]>(() => {
    const pluginEntries: PaletteEntry[] = pinnedPlugins.map((plugin) => ({
      id: plugin.id,
      group: "插件",
      label: plugin.name,
      run: () => onOpenPlugin(plugin.id),
    }));
    const commands: PaletteEntry[] = [
      { id: "summary", group: "命令", label: "生成今日站会摘要", run: () => onOpenPlugin("upcam") },
      { id: "market", group: "命令", label: "打开插件市场", run: () => { void navigate("/market"); } },
    ];
    const deviceEntries: PaletteEntry[] = devices.map((device) => ({
      id: device.id,
      group: "设备",
      label: device.remote ? device.name : `本机 · ${device.name}`,
      run: () => onPickDevice(device.id),
    }));
    const projectEntries: PaletteEntry[] = projects.map((project) => ({
      id: project.id,
      group: "项目",
      label: project.name,
      detail: project.id === activeProjectId ? "当前项目" : undefined,
      run: () => onSwitchProject(project.id),
    }));
    const all = [...pluginEntries, ...commands, ...deviceEntries, ...projectEntries];
    const needle = query.trim();
    return needle ? all.filter((item) => item.label.includes(needle) || item.group.includes(needle)) : all;
  }, [activeProjectId, navigate, onOpenPlugin, onPickDevice, onSwitchProject, projects, query]);

  useEffect(() => {
    setCursor(0);
  }, [query]);

  const groups = ["插件", "命令", "设备", "项目"] as const;

  return (
    <div className="palette-layer" role="presentation" onMouseDown={onClose}>
      <div className="palette" role="dialog" aria-label="命令条" onMouseDown={(event) => event.stopPropagation()}>
        <label className="palette-input">
          <input
            autoFocus
            aria-label="搜索插件、项目、设备或命令"
            placeholder="搜索插件、项目、设备或命令…"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === "Escape") onClose();
              if (event.key === "ArrowDown") {
                event.preventDefault();
                setCursor((value) => Math.min(value + 1, Math.max(entries.length - 1, 0)));
              }
              if (event.key === "ArrowUp") {
                event.preventDefault();
                setCursor((value) => Math.max(value - 1, 0));
              }
              if (event.key === "Enter" && entries[cursor]) {
                entries[cursor].run();
                onClose();
              }
            }}
          />
          <span className="keys"><Keycap>Ctrl</Keycap><Keycap>K</Keycap></span>
        </label>
        <div className="palette-list" role="listbox" aria-label="命令结果">
          {entries.length === 0 ? <p className="empty-inline">没有匹配的结果</p> : null}
          {groups.map((group) => {
            const items = entries.filter((item) => item.group === group);
            if (items.length === 0) return null;
            return (
              <section key={group}>
                <p>{group}</p>
                {items.map((item) => {
                  const index = entries.indexOf(item);
                  return (
                    <button
                      key={item.id}
                      type="button"
                      role="option"
                      aria-selected={index === cursor}
                      className={index === cursor ? "is-active" : ""}
                      onMouseEnter={() => setCursor(index)}
                      onClick={() => {
                        item.run();
                        onClose();
                      }}
                    >
                      <span>{item.label}</span>
                      {item.detail ? <small>{item.detail}</small> : null}
                      {index === cursor ? <kbd>Enter</kbd> : null}
                    </button>
                  );
                })}
              </section>
            );
          })}
        </div>
        <footer>
          <Keycap>↑</Keycap>
          <Keycap>↓</Keycap>
          <span>选择</span>
          <Keycap>Enter</Keycap>
          <span>打开</span>
          <Keycap>Esc</Keycap>
          <span>关闭</span>
        </footer>
      </div>
    </div>
  );
}
