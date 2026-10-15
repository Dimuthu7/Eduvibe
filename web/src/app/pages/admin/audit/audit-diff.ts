export interface FieldChange {
  field: string;
  from: string | null;
  to: string | null;
}

/** A stored value as text: nothing as null, strings as they are, everything else as JSON. */
function show(value: unknown): string | null {
  if (value === null || value === undefined) return null;
  return typeof value === 'string' ? value : JSON.stringify(value);
}

function parse(json: string | null): Record<string, unknown> {
  if (!json) return {};
  try {
    const value: unknown = JSON.parse(json);
    return value && typeof value === 'object' && !Array.isArray(value) ? (value as Record<string, unknown>) : {};
  } catch {
    return {};
  }
}

/** Housekeeping columns every row has; they say nothing about what the change was. */
const SKIPPED = new Set(['Id', 'CreatedAt', 'UpdatedAt', 'TenantId']);

/**
 * The fields an audit row changed, as the person reads them: a create shows what was set, a delete what was there, and an update
 * each field from its old value to its new one. The server stores only the fields that changed for an update.
 */
export function changesOf(before: string | null, after: string | null): FieldChange[] {
  const was = parse(before);
  const now = parse(after);
  const fields = [...new Set([...Object.keys(was), ...Object.keys(now)])].filter((f) => !SKIPPED.has(f));
  return fields.map((field) => ({ field, from: show(was[field]), to: show(now[field]) }));
}

/** "PlanFeature" as "Plan feature", for entities without a translated name. */
export const spaced = (name: string): string => name.replace(/([a-z])([A-Z])/g, (_, a: string, b: string) => `${a} ${b.toLowerCase()}`);
