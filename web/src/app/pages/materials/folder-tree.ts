import { MaterialFolder } from '../../core/api/materials.service';

/** The folders from the shelf down to this one, outermost first. Empty for the shelf itself (a null id). */
export function folderChain(id: string | null, folders: readonly MaterialFolder[]): MaterialFolder[] {
  const chain: MaterialFolder[] = [];
  for (let next = id; next !== null && chain.length < 16; ) {
    const folder = folders.find((f) => f.id === next);
    if (!folder) break;
    chain.unshift(folder);
    next = folder.parentId;
  }
  return chain;
}

/** "Term 1 / Week 2": where a folder sits, for lists where folders from different levels are mixed. */
export function folderLabel(id: string | null, folders: readonly MaterialFolder[]): string {
  return folderChain(id, folders).map((f) => f.name).join(' / ');
}

/** The folder and everything inside it, so a folder never offers to move into itself. */
export function withDescendants(id: string, folders: readonly MaterialFolder[]): Set<string> {
  const ids = new Set([id]);
  for (let grew = true; grew; ) {
    grew = false;
    for (const f of folders) {
      if (f.parentId !== null && ids.has(f.parentId) && !ids.has(f.id)) {
        ids.add(f.id);
        grew = true;
      }
    }
  }
  return ids;
}
