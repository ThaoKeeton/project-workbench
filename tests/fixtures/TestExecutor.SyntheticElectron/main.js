const { app, BrowserWindow } = require("electron");
const path = require("path");

// The debugger port arrives as --remote-debugging-port from the executor.
// A loading network folder can freeze the renderer; the executor waits on the CDP call.
app.commandLine.appendSwitch("remote-debugging-address", "127.0.0.1");
app.commandLine.appendSwitch("remote-allow-origins", "*");
app.commandLine.appendSwitch("force-device-scale-factor", "1");
app.disableHardwareAcceleration();
if (process.platform === "linux") {
  app.commandLine.appendSwitch("no-sandbox");
  app.commandLine.appendSwitch("disable-setuid-sandbox");
  app.commandLine.appendSwitch("ozone-platform", "x11");
  app.commandLine.appendSwitch("in-process-gpu");
}

function createWindow() {
  const window = new BrowserWindow({
    width: 960,
    height: 700,
    show: true,
    webPreferences: {
      contextIsolation: true,
      nodeIntegration: false
    }
  });
  window.setTitle("合成电子样品");
  window.loadFile(path.join(__dirname, "index.html")).catch((error) => {
    console.error(error);
    app.exit(1);
  });
}

app.whenReady().then(createWindow).catch((error) => {
  console.error(error);
  app.exit(1);
});
app.on("window-all-closed", () => app.quit());
