import { describe, expect, it } from "vitest";
import { API_VERSION } from "./protocol";
import { previewProbeScript, smokeFailures, smokeReport, type PageProbe } from "./smoke";

const page: PageProbe = {
  title: "项目工作台",
  sessionPresent: true,
  rail: true,
  commandBar: true,
  today: true,
  recentTiles: true,
  homeHeading: true,
  headingCodes: "26089,19978,22909,65292,25991,21069",
  builtins: true,
  product: "ProjectWorkbench",
  apiVersion: API_VERSION,
  autoSend: false,
  lanEnabled: false,
  catalogSchemaVersion: 1,
  preview: true,
  bind: "loopback",
  host: "local-service",
  minimumWidth: 1100,
};

describe("electron smoke report", () => {
  it("accepts the S1 shell only at the 1,100 floor", () => {
    const report = smokeReport(page, { minWidth: 1100, minHeight: 640, width: 1180, height: 800, ownedService: true });
    expect(smokeFailures(report)).toEqual([]);
    expect(smokeFailures({ ...report, windowMinWidth: 1099 })).toEqual(["window-floor"]);
    expect(smokeFailures({ ...report, autoSend: true, lanEnabled: true, catalogSchemaVersion: 2 })).toEqual(["auto-send", "lan", "catalog-schema"]);
    expect(previewProbeScript()).toContain(".greet h1");
    expect(previewProbeScript()).toContain("26089,19978,22909,65292,25991,21069");
    expect(previewProbeScript()).toContain("nav.rail");
    expect(previewProbeScript()).toContain("sessionStorage.getItem");
    expect(previewProbeScript()).not.toContain("session-token");
  });
});
