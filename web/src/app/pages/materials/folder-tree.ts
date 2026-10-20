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
