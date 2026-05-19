import { NextResponse } from "next/server";
import { getSupabaseAdminClient } from "@/lib/supabase";

function toTake(value: string | null): number {
  const parsed = Number.parseInt(value ?? "200", 10);
  if (Number.isNaN(parsed) || parsed <= 0) {
    return 200;
  }
  return Math.min(parsed, 1000);
}

export async function GET(request: Request) {
  try {
    const url = new URL(request.url);
    const discipline = url.searchParams.get("discipline")?.trim() ?? "";
    const kind = url.searchParams.get("kind")?.trim() ?? "";
    const category = url.searchParams.get("category")?.trim() ?? "";
    const q = url.searchParams.get("q")?.trim() ?? "";
    const take = toTake(url.searchParams.get("take"));

    const supabase = getSupabaseAdminClient();
    let query = supabase
      .schema("app")
      .from("families")
      .select(
        "family_id,family_name,category_name,rfa_path,preview_path,family_kind,approval_status,source_discipline,updated_at_utc"
      )
      .order("updated_at_utc", { ascending: false })
      .limit(take);

    if (discipline) {
      query = query.eq("source_discipline", discipline);
    }
    if (kind) {
      query = query.eq("family_kind", kind);
    }
    if (category) {
      query = query.eq("category_name", category);
    }
    if (q) {
      query = query.or(
        `family_name.ilike.%${q}%,category_name.ilike.%${q}%,rfa_path.ilike.%${q}%`
      );
    }

    const { data, error } = await query;
    if (error) {
      return NextResponse.json(
        { ok: false, error: error.message },
        { status: 500 }
      );
    }

    return NextResponse.json({
      ok: true,
      items:
        data?.map((row) => ({
          familyId: row.family_id,
          familyName: row.family_name,
          categoryName: row.category_name,
          rfaPath: row.rfa_path,
          previewPath: row.preview_path,
          familyKind: row.family_kind,
          approvalStatus: row.approval_status,
          sourceDiscipline: row.source_discipline,
          updatedAtUtc: row.updated_at_utc
        })) ?? []
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
