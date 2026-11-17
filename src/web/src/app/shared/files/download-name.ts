/**
 * The file name a download response carries in its Content-Disposition header (P13.55). A name with letters outside
 * ASCII (an Arabic title) is sent twice: `filename="____.xlsx"` for old clients, with the letters replaced, and
 * `filename*=UTF-8''…` with the real name. The real one wins; the plain one is used only when it is all there is.
 */
export function downloadFileName(contentDisposition: string | null | undefined, fallback: string): string {
  const header = contentDisposition ?? '';
  const encoded = /filename\*\s*=\s*(?:UTF-8|utf-8)''([^;]+)/.exec(header);
  if (encoded) {
    try {
      const name = decodeURIComponent(encoded[1].trim().replace(/^"|"$/g, ''));
      if (name) {
        return name;
      }
    } catch {
      // A name that is not valid percent-encoding: fall through to the plain one.
    }
  }
  const plain = /filename\s*=\s*(?:"([^"]*)"|([^;]+))/i.exec(header);
  const name = (plain?.[1] ?? plain?.[2] ?? '').trim();
  return name || fallback;
}
