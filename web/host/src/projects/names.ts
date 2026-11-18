export type NamedProject = { id: string; name: string };

export function activeName(projects: readonly NamedProject[], activeProjectId: string): string {
  return projects.find((project) => project.id === activeProjectId)?.name ?? "当前项目";
}

export function sameProjectName(left: string, right: string): boolean {
  return left.toLocaleLowerCase("en-US") === right.toLocaleLowerCase("en-US");
}

/** Mirrors the catalog name rule: trimmed, 1–80 UTF-16 units, no controls, valid Unicode. */
export function nameFeedback(name: string, projects: readonly NamedProject[], exceptId?: string): string {
  if (!validUnicode(name)) return "这个名称用不了。请换成没有换行、80 个字以内的名称。";
  if (hasControl(name)) return "名称里不能有换行。";
  const trimmed = name.trim();
  if (trimmed.length === 0) return "请先写一个项目名称。";
  if (trimmed.length > 80) return "名称最多 80 个字。";
  const taken = projects.find((project) => project.id !== exceptId && sameProjectName(project.name, trimmed));
  if (taken) return `已经有叫「${taken.name}」的项目。`;
  return "";
}

function hasControl(value: string): boolean {
  for (let index = 0; index < value.length; index += 1) {
    const code = value.charCodeAt(index);
    if (code <= 31 || (code >= 127 && code <= 159)) return true;
  }
  return false;
}

function validUnicode(value: string): boolean {
  for (let index = 0; index < value.length; index += 1) {
    const code = value.charCodeAt(index);
    if (code < 0xd800 || code > 0xdfff) continue;
    const next = value.charCodeAt(index + 1);
    if (code > 0xdbff || index + 1 === value.length || next < 0xdc00 || next > 0xdfff) return false;
    index += 1;
  }
  return true;
}
