import { describe, expect, it } from "vitest";
import { activeName, nameFeedback, sameProjectName } from "./names";

const projects = [
  { id: "default", name: "星河协作" },
  { id: "lamp", name: "灯塔实验" },
];

describe("project names", () => {
  it("treats surrounding spaces and letter case as the same name", () => {
    expect(sameProjectName(" 星河协作 ", "星河协作")).toBe(false);
    expect(sameProjectName("星河协作", "星河协作")).toBe(true);
    expect(sameProjectName("Demo", "demo")).toBe(true);
  });

  it("asks for a name when the field is blank", () => {
    expect(nameFeedback("", projects)).toBe("请先写一个项目名称。");
    expect(nameFeedback("   ", projects)).toBe("请先写一个项目名称。");
  });

  it("rejects controls, broken characters, and names over 80", () => {
    expect(nameFeedback("北\n港", projects)).toBe("名称里不能有换行。");
    expect(nameFeedback("北\u0000港", projects)).toBe("名称里不能有换行。");
    expect(nameFeedback(`北${String.fromCharCode(0xd800)}`, projects)).toBe("这个名称用不了。请换成没有换行、80 个字以内的名称。");
    expect(nameFeedback("名".repeat(81), projects)).toBe("名称最多 80 个字。");
    expect(nameFeedback("名".repeat(80), projects)).toBe("");
    expect(nameFeedback("海岸 🧪", projects)).toBe("");
  });

  it("points at the existing project when the name is already used", () => {
    expect(nameFeedback(" 灯塔实验 ", projects)).toBe("已经有叫「灯塔实验」的项目。");
    expect(nameFeedback("星河协作", projects, "default")).toBe("");
    expect(nameFeedback("星河协作", projects, "lamp")).toBe("已经有叫「星河协作」的项目。");
  });

  it("uses the current project name when the id is known", () => {
    expect(activeName(projects, "lamp")).toBe("灯塔实验");
    expect(activeName(projects, "missing")).toBe("当前项目");
  });
});
