/** Puts values into text such as "{count} students asked to join {class}". A name with no value is left as it is. */
export function fill(text: string, values: Record<string, string | number>): string {
  return text.replace(/\{(\w+)\}/g, (whole, name: string) => (name in values ? String(values[name]) : whole));
}
