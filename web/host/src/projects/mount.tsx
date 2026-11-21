import { createRoot, type Root } from "react-dom/client";
import { ProjectDeskClient } from "./client";
import { ProjectDesk } from "./ProjectDesk";
import { applyTheme, readStoredSetting } from "../theme";

export function mountProjectDesk(root: HTMLElement): Root {
  applyTheme(readStoredSetting());
  const token = sessionStorage.getItem("workbench.session") ?? "";
  const view = createRoot(root);
  if (!token) view.render(<p role="alert">未找到本机会话。请从工作台外壳打开。</p>);
  else view.render(<ProjectDesk client={new ProjectDeskClient(token)} />);
  return view;
}
