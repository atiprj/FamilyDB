import { NextResponse } from "next/server";
import { getSupabaseAdminClient } from "@/lib/supabase";
import { requireAddinApiKey } from "@/lib/api-auth";

export async function GET(request: Request) {
  const authError = requireAddinApiKey(request);
  if (authError) {
    return authError;
  }

  try {
    const supabase = getSupabaseAdminClient();
    const { count, error } = await supabase
      .schema("app")
      .from("web_to_revit_queue")
      .select("*", { count: "exact", head: true })
      .eq("status", "Pending");

    if (error) {
      return NextResponse.json(
        { ok: false, error: error.message },
        { status: 500 }
      );
    }

    return NextResponse.json({
      ok: true,
      pending: count ?? 0
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
