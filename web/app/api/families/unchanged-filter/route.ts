import { NextResponse } from "next/server";
import { requireAddinApiKey } from "@/lib/api-auth";
import { getSupabaseAdminClient } from "@/lib/supabase";
import { isPreviewUrl } from "@/lib/preview";

type FilterItem = {
  rfaPath?: string;
  fileHash?: string | null;
};

type FilterBody = {
  items?: FilterItem[];
};

export async function POST(request: Request) {
  const authError = requireAddinApiKey(request);
  if (authError) {
    return authError;
  }

  try {
    const body = (await request.json()) as FilterBody;
    const items = body?.items ?? [];
    if (items.length === 0) {
      return NextResponse.json({
        ok: true,
        unchangedRfaPaths: [],
        needsPreviewOnlyRfaPaths: []
      });
    }

    const valid = items
      .map((i) => ({
        rfaPath: i.rfaPath?.trim() ?? "",
        fileHash: (i.fileHash ?? "").trim()
      }))
      .filter((i) => i.rfaPath.length > 0);

    const supabase = getSupabaseAdminClient();
    const unchangedRfaPaths: string[] = [];
    const needsPreviewOnlyRfaPaths: string[] = [];
    const chunkSize = 80;

    for (let i = 0; i < valid.length; i += chunkSize) {
      const chunk = valid.slice(i, i + chunkSize);
      const paths = chunk.map((c) => c.rfaPath);
      const { data, error } = await supabase
        .schema("app")
        .from("families")
        .select("rfa_path,file_hash,preview_path")
        .in("rfa_path", paths);

      if (error) {
        return NextResponse.json({ ok: false, error: error.message }, { status: 500 });
      }

      const byPath = new Map(
        (data ?? []).map((row) => [
          row.rfa_path as string,
          {
            fileHash: (row.file_hash ?? "").trim(),
            previewPath: row.preview_path as string | null
          }
        ])
      );

      for (const item of chunk) {
        const existing = byPath.get(item.rfaPath);
        if (!existing) {
          continue;
        }

        if (
          item.fileHash.length > 0 &&
          existing.fileHash.length > 0 &&
          item.fileHash === existing.fileHash
        ) {
          unchangedRfaPaths.push(item.rfaPath);
          if (!isPreviewUrl(existing.previewPath)) {
            needsPreviewOnlyRfaPaths.push(item.rfaPath);
          }
        }
      }
    }

    return NextResponse.json({
      ok: true,
      unchangedRfaPaths,
      needsPreviewOnlyRfaPaths
    });
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
