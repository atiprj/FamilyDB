import { NextResponse } from "next/server";
import { getSupabaseAdminClient } from "@/lib/supabase";
import { requireAddinApiKey } from "@/lib/api-auth";

export async function POST(request: Request) {
  const authError = requireAddinApiKey(request);
  if (authError) {
    return authError;
  }

  try {
    const body = (await request.json()) as { familyId?: number };
    const familyId = Number(body?.familyId);
    if (!Number.isInteger(familyId) || familyId <= 0) {
      return NextResponse.json(
        { ok: false, error: "Invalid familyId" },
        { status: 400 }
      );
    }

    const supabase = getSupabaseAdminClient();
    const { data, error } = await supabase
      .schema("app")
      .from("web_to_revit_queue")
      .insert({ family_id: familyId, status: "Pending" })
      .select("queue_id")
      .single();

    if (error) {
      return NextResponse.json(
        { ok: false, error: error.message },
        { status: 500 }
      );
    }

    return NextResponse.json({
      ok: true,
      queueId: data?.queue_id ?? null,
      message: "Queued for Revit processing"
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
