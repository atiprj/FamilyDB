import { NextResponse } from "next/server";
import { requireAddinApiKey } from "@/lib/api-auth";
import { uploadFamilyPreview } from "@/lib/storage";

type UploadBody = {
  rfaPath?: string;
  imageBase64?: string;
  contentType?: string;
};

export async function POST(request: Request) {
  const authError = requireAddinApiKey(request);
  if (authError) {
    return authError;
  }

  try {
    const body = (await request.json()) as UploadBody;
    const rfaPath = body.rfaPath?.trim() ?? "";
    const imageBase64 = body.imageBase64?.trim() ?? "";

    if (!rfaPath || !imageBase64) {
      return NextResponse.json(
        { ok: false, error: "rfaPath and imageBase64 are required" },
        { status: 400 }
      );
    }

    if (imageBase64.length > 700_000) {
      return NextResponse.json(
        { ok: false, error: "Image too large (max ~512KB raw)" },
        { status: 413 }
      );
    }

    const bytes = Buffer.from(imageBase64, "base64");
    if (bytes.length === 0) {
      return NextResponse.json(
        { ok: false, error: "Invalid base64 image" },
        { status: 400 }
      );
    }

    const contentType = body.contentType?.trim() || "image/png";
    const { publicUrl, objectKey } = await uploadFamilyPreview(rfaPath, bytes, contentType);

    return NextResponse.json({
      ok: true,
      previewUrl: publicUrl,
      objectKey
    });
  } catch (err) {
    const message = err instanceof Error ? err.message : "Unknown error";
    if (message.toLowerCase().includes("bucket")) {
      return NextResponse.json(
        {
          ok: false,
          error:
            "Storage bucket missing. Run docs/supabase/002_storage_previews.sql in Supabase."
        },
        { status: 500 }
      );
    }

    return NextResponse.json({ ok: false, error: message }, { status: 500 });
  }
}
