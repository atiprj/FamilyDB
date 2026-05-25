import { NextResponse } from "next/server";
import { requireAddinApiKey } from "@/lib/api-auth";
import { uploadFamilyPreview } from "@/lib/storage";
import { getSupabaseAdminClient } from "@/lib/supabase";

type BatchItem = {
  rfaPath?: string;
  imageBase64?: string;
  contentType?: string;
};

type BatchBody = {
  items?: BatchItem[];
};

export async function POST(request: Request) {
  const authError = requireAddinApiKey(request);
  if (authError) {
    return authError;
  }

  try {
    const body = (await request.json()) as BatchBody;
    const items = body?.items ?? [];
    if (items.length === 0) {
      return NextResponse.json({ ok: true, uploaded: 0, results: [] });
    }

    if (items.length > 20) {
      return NextResponse.json(
        { ok: false, error: "Max 20 previews per batch request" },
        { status: 400 }
      );
    }

    const supabase = getSupabaseAdminClient();
    const results: {
      rfaPath: string;
      ok: boolean;
      previewUrl?: string;
      error?: string;
    }[] = [];

    for (const item of items) {
      const rfaPath = item.rfaPath?.trim() ?? "";
      const imageBase64 = item.imageBase64?.trim() ?? "";

      if (!rfaPath || !imageBase64) {
        results.push({ rfaPath, ok: false, error: "Missing rfaPath or imageBase64" });
        continue;
      }

      if (imageBase64.length > 700_000) {
        results.push({ rfaPath, ok: false, error: "Image too large" });
        continue;
      }

      try {
        const bytes = Buffer.from(imageBase64, "base64");
        const contentType = item.contentType?.trim() || "image/png";
        const { publicUrl } = await uploadFamilyPreview(rfaPath, bytes, contentType);

        await supabase
          .schema("app")
          .from("families")
          .update({ preview_path: publicUrl })
          .eq("rfa_path", rfaPath);

        results.push({ rfaPath, ok: true, previewUrl: publicUrl });
      } catch (err) {
        results.push({
          rfaPath,
          ok: false,
          error: err instanceof Error ? err.message : "Upload failed"
        });
      }
    }

    const uploaded = results.filter((r) => r.ok).length;
    return NextResponse.json({ ok: uploaded > 0, uploaded, results });
  } catch (err) {
    return NextResponse.json(
      {
        ok: false,
        error: err instanceof Error ? err.message : "Unknown error"
      },
      { status: 500 }
    );
  }
}
