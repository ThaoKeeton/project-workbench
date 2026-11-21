import { cloneElement, isValidElement, useEffect, useMemo, useRef, useState, type ReactElement, type ReactNode } from "react";
import { Link, useNavigate } from "react-router-dom";
import { devices, pinnedPlugins, type DeviceId, type IconName } from "./catalog";
import { formatDailySummary } from "./executor/model";
import { Icon } from "./icons";
import { Keycap } from "./shell";

export type PaletteEntry = {
  id: string;
  group: "插件" | "命令" | "设备" | "项目";
  label: string;
  hint?: string;
  detail?: string;
  unavailable?: boolean;
  run: () => void;
};

export function HomePage({
  onOpenPalette,
  palette,
  plugins = pinnedPlugins,
  pinned = pinnedPlugins.map((plugin) => plugin.id),
  recent = [],
  onOpen,
  onPin,
  unopened = [],
}: {
  onOpenPalette: () => void;
  palette?: ReactNode;
  plugins?: Array<{ id: string; name: string; icon: IconName; hint: string; loaded?: boolean; state?: string; pinnable?: boolean }>;
  pinned?: string[];
  recent?: Array<{ id: string; at: string }>;
  onOpen?: (id: string) => void;
  onPin?: (id: string, pinned: boolean) => void;
  unopened?: string[];
}) {
  const navigate = useNavigate();
  const recentPlugins = recent
    .map((item) => plugins.find((plugin) => plugin.id === item.id))
    .filter((plugin): plugin is (typeof plugins)[number] => plugin !== undefined);
  const open = (id: string) => { if (onOpen) onOpen(id); else void navigate(`/plugin/${id}`); };
  const paletteNode = isValidElement(palette)
    ? cloneElement(palette as ReactElement<{ recent?: string[]; plugins?: typeof plugins }>, {
        recent: recent.map((item) => item.id),
        plugins,
      })
    : palette;
  return (
    <div className="launcher">
      {paletteNode ?? (
        <button type="button" className="command-bar" onClick={onOpenPalette}>
          <Icon name="home" size={18} />
          <span>搜索插件、项目、设备或命令…</span>
          <span className="shortcut">Ctrl K</span>
        </button>
      )}
      {paletteNode ? null : (
        <>
          {dailySummary() ? (
            <section className="column">
              <h2 className="section">今天</h2>
              <ul className="today">
                <li>
                  <Link className="today-link" to="/plugin/runner?filter=todo">
                    <span className="time"><Icon name="activity" size={18} /></span>
                    <span className="source">自动测试</span>
                    <span>{dailySummary()}</span>
                  </Link>
                </li>
              </ul>
            </section>
          ) : null}
          <section className="column recent-open">
            <h2 className="section">最近打开</h2>
            {recentPlugins.length === 0 ? <p>还没有打开过。从下面选一个，关掉再打开也会记得。</p> : (
              <div className="tiles">
                {recentPlugins.map((plugin) => (
                  <PluginTile key={plugin.id} id={plugin.id} name={plugin.name} hint={plugin.hint} icon={plugin.icon} onOpen={open} />
                ))}
              </div>
            )}
          </section>
          <section className="column local-plugins">
            <h2 className="section">这台电脑上的插件</h2>
            <ul className="catalog">
              {plugins.map((plugin) => {
                const held = pinned.includes(plugin.id);
                const loaded = plugin.loaded !== false;
                return (
                  <li key={plugin.id}>
                    <button type="button" className="catalog-main" onClick={() => open(plugin.id)}>
                      <Icon name={plugin.icon} />
                      <span>
                        <strong>{plugin.name}</strong>
                        <small>{plugin.state ?? plugin.hint}</small>
                      </span>
                    </button>
                    {onPin && loaded && plugin.pinnable !== false ? (
                      <button type="button" className="btn" aria-pressed={held} aria-label={held ? `从左侧拿下 ${plugin.name}` : `把 ${plugin.name} 固定到左侧`} onClick={() => onPin(plugin.id, !held)}>
                        {held ? "从左侧拿下" : "固定到左侧"}
                      </button>
                    ) : null}
                  </li>
                );
              })}
            </ul>
            {unopened.length > 0 ? <p>另外发现 {unopened.join("、")}，没有可以打开的页面。</p> : null}
          </section>
        </>
      )}
    </div>
  );
}

function dailySummary(): string | null {
  try {
    const raw = window.sessionStorage.getItem("workbench.author.summary");
    if (!raw) return null;
    const counts = JSON.parse(raw) as { ran?: number; passed?: number; failed?: number; needsUpdate?: number };
    if (!Number.isFinite(counts.ran)) return null;
    return formatDailySummary({ ran: counts.ran ?? 0, passed: counts.passed ?? 0, failed: counts.failed ?? 0, needsUpdate: counts.needsUpdate ?? 0 });
  } catch {
    return null;
  }
}

function PluginTile({ id, name, hint, icon, onOpen }: { id: string; name: string; hint: string; icon: IconName; onOpen?: (id: string) => void }) {
  const navigate = useNavigate();
  return (
    <button type="button" className="tile" onClick={() => { if (onOpen) onOpen(id); else void navigate(`/plugin/${id}`); }}>
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
  plugins = pinnedPlugins,
  recent = [],
}: {
  projects: Array<{ id: string; name: string }>;
  activeProjectId: string;
  onClose: () => void;
  onOpenPlugin: (id: string) => void;
  onPickDevice: (id: DeviceId) => void;
  onSwitchProject: (id: string) => void;
  plugins?: Array<{ id: string; name: string; hint?: string; loaded?: boolean; state?: string }>;
  recent?: string[];
}) {
  const navigate = useNavigate();
  const input = useRef<HTMLInputElement>(null);
  const [query, setQuery] = useState("");
  const [cursor, setCursor] = useState(0);
  const [note, setNote] = useState("");
  const entries = useMemo<PaletteEntry[]>(() => {
    const order = new Map(recent.map((id, index) => [id, index]));
    const pluginEntries: PaletteEntry[] = [...plugins]
      .sort((left, right) => (order.get(left.id) ?? 100) - (order.get(right.id) ?? 100))
      .map((plugin) => {
        const unavailable = plugin.loaded === false;
        return {
          id: plugin.id,
          group: "插件" as const,
          label: plugin.name,
          hint: plugin.hint,
          detail: unavailable ? (plugin.state ?? "没能载入") : order.has(plugin.id) ? "最近打开" : undefined,
          unavailable,
          run: () => onOpenPlugin(plugin.id),
        };
      });
    const standup = plugins.find((plugin) => plugin.id === "upcam");
    const commands: PaletteEntry[] = [
      {
        id: "summary",
        group: "命令",
        label: "生成今日站会摘要",
        hint: "打开站会日报",
        detail: standup && standup.loaded !== false ? undefined : "这台电脑上没有可用的站会日报",
        unavailable: !(standup && standup.loaded !== false),
        run: () => onOpenPlugin("upcam"),
      },
      { id: "market", group: "命令", label: "打开这台电脑上的插件", hint: "查看已经装在这里的插件", run: () => { void navigate("/market"); } },
    ];
    const deviceEntries: PaletteEntry[] = devices.map((device) => ({
      id: device.id,
      group: "设备" as const,
      label: device.remote ? device.name : `本机 · ${device.name}`,
      hint: device.status,
      detail: device.remote ? "这台预览连不上别的电脑" : undefined,
      unavailable: device.remote,
      run: () => onPickDevice(device.id),
    }));
    const projectEntries: PaletteEntry[] = projects.map((project) => ({
      id: project.id,
      group: "项目" as const,
      label: project.name,
      detail: project.id === activeProjectId ? "当前项目" : undefined,
      run: () => onSwitchProject(project.id),
    }));
    const all = [...pluginEntries, ...commands, ...deviceEntries, ...projectEntries];
    const needle = query.trim().toLocaleLowerCase();
    if (!needle) return all;
    return all.filter((item) => `${item.label} ${item.hint ?? ""}`.toLocaleLowerCase().includes(needle));
  }, [activeProjectId, navigate, onOpenPlugin, onPickDevice, onSwitchProject, plugins, projects, query, recent]);

  useEffect(() => {
    setCursor(0);
  }, [query]);

  const choose = (item: PaletteEntry | undefined) => {
    if (!item) {
      setNote(query.trim() ? "没有匹配的结果。换个词，或按 Esc 清空。" : "先选一项，再按 Enter 打开。");
      return;
    }
    if (item.unavailable) {
      setNote(item.detail ?? `「${item.label}」现在打不开。`);
      return;
    }
    item.run();
    onClose();
  };

  const groups = ["插件", "命令", "设备", "项目"] as const;

  return (
    <div className="palette-layer" role="presentation" onMouseDown={onClose}>
      <div
        className="palette"
        role="dialog"
        aria-label="命令条"
        onMouseDown={(event) => event.stopPropagation()}
        onKeyDown={(event) => {
          if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") {
            event.preventDefault();
            input.current?.focus();
          }
        }}
      >
        <label className="palette-input">
          <input
            ref={input}
            autoFocus
            aria-label="搜索插件、项目、设备或命令"
            placeholder="搜索插件、项目、设备或命令…"
            value={query}
            onChange={(event) => { setQuery(event.target.value); setNote(""); }}
            onKeyDown={(event) => {
              if (event.key === "Escape") {
                event.preventDefault();
                if (query) { setQuery(""); setNote(""); return; }
                onClose();
              }
              if (event.key === "ArrowDown") {
                event.preventDefault();
                setCursor((value) => Math.min(value + 1, Math.max(entries.length - 1, 0)));
              }
              if (event.key === "ArrowUp") {
                event.preventDefault();
                setCursor((value) => Math.max(value - 1, 0));
              }
              if (event.key === "Enter") {
                event.preventDefault();
                choose(entries[cursor]);
              }
            }}
          />
          <span className="keys"><Keycap>Ctrl</Keycap><Keycap>K</Keycap></span>
        </label>
        <div className="palette-list" role="listbox" aria-label="命令结果">
          {note ? <p className="empty-inline" role="status">{note}</p> : null}
          {!note && entries.length === 0 ? <p className="empty-inline" role="status">没有匹配的结果。换个词，或按 Esc 清空。</p> : null}
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
                      key={`${item.group}-${item.id}`}
                      type="button"
                      role="option"
                      aria-selected={index === cursor}
                      aria-disabled={item.unavailable || undefined}
                      className={index === cursor ? "is-active" : ""}
                      onMouseEnter={() => setCursor(index)}
                      onClick={() => choose(item)}
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
          <span>清空或关闭</span>
        </footer>
      </div>
    </div>
  );
}
