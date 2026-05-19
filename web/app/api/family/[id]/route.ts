import { NextResponse } from "next/server";
import { getSupabaseAdminClient } from "@/lib/supabase";

type Params = {
  params: Promise<{ id: string }>;
};

export async function GET(_request: Request, { params }: Params) {
  try {
    const { id } = await params;
    const familyId = Number.parseInt(id, 10);
    if (Number.isNaN(familyId) || familyId <= 0) {
      return NextResponse.json(
        { ok: false, error: "Invalid family id" },
        { status: 400 }
      );
    }

    const supabase = getSupabaseAdminClient();

    const [{ data: family, error: familyError }, { data: parameters, error: paramsError }] =
      await Promise.all([
        supabase
          .schema("app")
          .from("families")
          .select("*")
          .eq("family_id", familyId)
          .maybeSingle(),
        supabase
          .schema("app")
          .from("parameters")
          .select("*")
          .eq("family_id", familyId)
          .order("parameter_name", { ascending: true })
      ]);

    if (familyError) {
      return NextResponse.json(
        { ok: false, error: familyError.message },
        { status: 500 }
      );
    }
    if (paramsError) {
      return NextResponse.json(
        { ok: false, error: paramsError.message },
        { status: 500 }
      );
    }
    if (!family) {
      return NextResponse.json(
        { ok: false, error: "Family not found" },
        { status: 404 }
      );
    }

    return NextResponse.json({
      ok: true,
      family,
      parameters: parameters ?? []
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
