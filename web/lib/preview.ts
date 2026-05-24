export function isPreviewUrl(path: string | null | undefined): boolean {
  if (!path) {
    return false;
  }
  return path.startsWith("http://") || path.startsWith("https://");
}
