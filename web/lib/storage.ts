import { createHash } from "crypto";
import { getSupabaseAdminClient } from "@/lib/supabase";

export const FAMILY_PREVIEWS_BUCKET = "family-previews";

export function previewObjectKey(rfaPath: string): string {
  const hash = createHash("sha256").update(rfaPath.trim()).digest("hex").slice(0, 40);
  return `thumbs/${hash}.png`;
}

export async function uploadFamilyPreview(
  rfaPath: string,
  bytes: Buffer,
  contentType = "image/png"
): Promise<{ publicUrl: string; objectKey: string }> {
  const supabase = getSupabaseAdminClient();
  const objectKey = previewObjectKey(rfaPath);

  const { error } = await supabase.storage
    .from(FAMILY_PREVIEWS_BUCKET)
    .upload(objectKey, bytes, {
      upsert: true,
      contentType,
      cacheControl: "3600"
    });

  if (error) {
    throw new Error(error.message);
  }

  const { data } = supabase.storage.from(FAMILY_PREVIEWS_BUCKET).getPublicUrl(objectKey);
  return { publicUrl: data.publicUrl, objectKey };
}
