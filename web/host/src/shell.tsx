import type { ReactNode } from "react";
import { NavLink, useLocation } from "react-router-dom";
import { deviceLabel, devices, type DeviceId, type IconName } from "./catalog";
import { Icon, WindowGlyph } from "./icons";

const railBottom: Array<{ to: string; label: string; icon: IconName }> = [
  { to: "/market", label: "市场", icon: "store" },
  { to: "/devices", label: "设备", icon: "devices" },
  { to: "/diagnostics", label: "诊断", icon: "diagnostics" },
  { to: "/settings", label: "设置", icon: "settings" },
];

export function Shell({
  projectName,
  activeProjectId,
  projects,
  projectOpen,
  onToggleProject,
  onSwitchProject,
  onManageProject,
  deviceId,
  deviceOpen,
  onToggleDevice,
  onPickDevice,
  onPair,
  plugins,
  onOpenPlugin,
  notice,
  onRetryNotice,
  remote,
  children,
}: {
  projectName: string;
  activeProjectId: string;
  projects: Array<{ id: string; name: string }>;
  projectOpen: boolean;
  onToggleProject: () => void;
  onSwitchProject: (id: string) => void;
  onManageProject: () => void;
  deviceId: DeviceId;
  deviceOpen: boolean;
  onToggleDevice: () => void;
  onPickDevice: (id: DeviceId) => void;
  onPair: () => void;
  plugins: Array<{ id: string; name: string; icon: IconName }>;
  onOpenPlugin: (id: string) => void;
  notice?: string;
  onRetryNotice?: () => void;
  remote: boolean;
  children: ReactNode;
}) {
  const location = useLocation();
  return (
    <div className="shell">
      <header className="topbar">
        <div className="wordmark">Project Workbench</div>
        <div className="top-grow" />
        {remote ? <span className="remote-pill"><i />远程会话</span> : null}
        <div className="top-context">
          <div className="project">
            <button type="button" className="context-project device-trigger" aria-haspopup="menu" aria-expanded={projectOpen} aria-label={`当前项目 ${projectName}`} onClick={onToggleProject}>
              {projectName} ▾
            </button>
            {projectOpen ? (
              <div className="device-menu project-menu" role="menu" aria-label="切换项目">
                {projects.map((project) => (
                  <button key={project.id} type="button" role="menuitem" className={project.id === activeProjectId ? "is-current" : ""} onClick={() => onSwitchProject(project.id)}>
                    <span>
                      <strong>{project.name}</strong>
                      {project.id === activeProjectId ? <small>当前</small> : null}
                    </span>
                  </button>
                ))}
                <button type="button" role="menuitem" className="pair" onClick={onManageProject}>新建或改名</button>
              </div>
            ) : null}
          </div>
          <span className="context-dot" aria-hidden="true">·</span>
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
          {plugins.map((plugin) => (
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
        <div className="shell-main">
          {notice ? (
            <div className="desk-note">
              <p role="status">{notice}</p>
              {onRetryNotice ? <button type="button" className="btn" onClick={onRetryNotice}>再试一次</button> : null}
            </div>
          ) : null}
          {children}
        </div>
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
