import type { ReactNode } from "react";
import { NavLink, useLocation } from "react-router-dom";
import { deviceLabel, devices, pinnedPlugins, type DeviceId, type IconName, type PluginId } from "./catalog";
import { Icon, WindowGlyph } from "./icons";

const railBottom: Array<{ to: string; label: string; icon: IconName }> = [
  { to: "/market", label: "市场", icon: "store" },
  { to: "/devices", label: "设备", icon: "devices" },
  { to: "/diagnostics", label: "诊断", icon: "diagnostics" },
  { to: "/settings", label: "设置", icon: "settings" },
];

export function Shell({
  projectName,
  deviceId,
  deviceOpen,
  onToggleDevice,
  onPickDevice,
  onPair,
  onOpenPlugin,
  remote,
  children,
}: {
  projectName: string;
  deviceId: DeviceId;
  deviceOpen: boolean;
  onToggleDevice: () => void;
  onPickDevice: (id: DeviceId) => void;
  onPair: () => void;
  onOpenPlugin: (id: PluginId) => void;
  remote: boolean;
  children: ReactNode;
}) {
  const location = useLocation();
  return (
    <div className="shell">
      <header className="topbar">
        <div className="project-name">
          <Icon name="folder" />
          <span>{projectName}</span>
        </div>
        <div className="top-grow" />
        {remote ? <span className="remote-pill"><i />远程会话</span> : null}
        <div className="device">
          <button type="button" className="device-trigger" aria-expanded={deviceOpen} aria-haspopup="menu" onClick={onToggleDevice}>
            <Icon name="laptop" />
            <span>{deviceLabel(deviceId)} ▾</span>
          </button>
          {deviceOpen ? (
            <div className="device-menu" role="menu">
              {["本机", "局域网"].map((group) => (
                <section key={group}>
                  <p className="menu-label">{group}</p>
                  {devices.filter((item) => item.group === group).map((item) => (
                    <button key={item.id} type="button" role="menuitem" className={item.id === deviceId ? "is-current" : ""} onClick={() => onPickDevice(item.id)}>
                      <Icon name={item.icon} />
                      <span>
                        <strong>{item.name}</strong>
                        <small><i className={`status-dot ${item.dot}`} />{item.status}</small>
                      </span>
                    </button>
                  ))}
                </section>
              ))}
              <button type="button" className="pair" onClick={onPair}>配对新设备</button>
            </div>
          ) : null}
        </div>
        <div className="window-controls" aria-hidden="false">
          <button type="button" aria-label="最小化"><WindowGlyph kind="min" /></button>
          <button type="button" aria-label="最大化"><WindowGlyph kind="max" /></button>
          <button type="button" className="close" aria-label="关闭"><WindowGlyph kind="close" /></button>
        </div>
      </header>
      <div className="shell-body">
        <nav className="rail" aria-label="主导航">
          <RailLink to="/" label="首页" icon="home" end />
          {pinnedPlugins.map((plugin) => (
            <button
              key={plugin.id}
              type="button"
              className="rail-btn"
              aria-label={plugin.name}
              aria-current={location.pathname.startsWith(`/plugin/${plugin.id}`) ? "page" : undefined}
              onClick={() => onOpenPlugin(plugin.id)}
            >
              <span className="rail-hit"><Icon name={plugin.icon} size={20} /></span>
              <span className="rail-tip" role="tooltip">{plugin.name}</span>
            </button>
          ))}
          <span className="rail-grow" />
          {railBottom.map((item) => (
            <RailLink key={item.to} to={item.to} label={item.label} icon={item.icon} />
          ))}
        </nav>
        <div className="shell-main">{children}</div>
      </div>
    </div>
  );
}

function RailLink({ to, label, icon, end }: { to: string; label: string; icon: IconName; end?: boolean }) {
  return (
    <NavLink to={to} end={end} className="rail-btn" aria-label={label}>
      <span className="rail-hit"><Icon name={icon} size={20} /></span>
      <span className="rail-tip" role="tooltip">{label}</span>
    </NavLink>
  );
}

export function Keycap({ children }: { children: string }) {
  return <kbd className="keycap">{children}</kbd>;
}
