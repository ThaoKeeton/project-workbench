const { ipcRenderer } = require("electron");

const token = ipcRenderer.sendSync("workbench:session-token");
if (typeof token === "string" && /^[0-9A-Za-z-]{16,128}$/.test(token) && location.hostname === "127.0.0.1") {
  sessionStorage.setItem("workbench.session", token);
}
