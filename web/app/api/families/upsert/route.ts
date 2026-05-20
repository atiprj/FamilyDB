import { NextResponse } from "next/server";
import { getSupabaseAdminClient } from "@/lib/supabase";
import { requireAddinApiKey } from "@/lib/api-auth";

type IncomingFamily = {
  familyName?: string;
  categoryName?: string | null;
  rfaPath?: string;
  previewPath?: string | null;
  revitVersion?: number | null;
  fileHash?: string | null;
  approvalStatus?: string | null;
  familyKind?: string | null;
  sourceModelPath?: string | null;
  sourceElementTypeId?: number | null;
  sourceDiscipline?: string | null;
};

type IncomingParameter = {
  parameterName?: string;
  parameterGroupName?: string | null;
  storageType?: string | null;
  stringValue?: string | null;
  numberValue?: number | null;
  integerValue?: number | null;
  elementIdValue?: number | null;
  isInstance?: boolean;
  isShared?: boolean;
  unitType?: string | null;
};

type IncomingItem = {
  family?: IncomingFamily;
  parameters?: IncomingParameter[];
};

type UpsertBody = {
  items?: IncomingItem[];
};

function isValidItem(item: IncomingItem | undefined): item is IncomingItem {
  const family = item?.family;
  if (!family) {
    return false;
  }

  const familyName = family.familyName?.trim() ?? "";
  const rfaPath = family.rfaPath?.trim() ?? "";
  return familyName.length > 0 && rfaPath.length > 0;
}

export async function POST(request: Request) {
  const authError = requireAddinApiKey(request);
  if (authError) {
    return authError;
  }

  try {
    const body = (await request.json()) as UpsertBody;
    const items = body?.items ?? [];
    if (items.length === 0) {
      return NextResponse.json(
        { ok: false, error: "No items provided" },
        { status: 400 }
      );
    }

    const supabase = getSupabaseAdminClient();
    let upserted = 0;
    let failed = 0;
    const errors: string[] = [];

    for (let i = 0; i < items.length; i++) {
      const item = items[i];
      if (!isValidItem(item)) {
        failed++;
        errors.push(`Item ${i}: missing familyName or rfaPath`);
        continue;
      }

      const family = item.family!;
      const parameters = item.parameters ?? [];

      const familyPayload = {
        family_name: family.familyName!.trim(),
        category_name: family.categoryName?.trim() || null,
        rfa_path: family.rfaPath!.trim(),
        preview_path: family.previewPath?.trim() || null,
        revit_version: Number.isInteger(family.revitVersion)
          ? family.revitVersion
          : null,
        file_hash: family.fileHash?.trim() || null,
        approval_status: family.approvalStatus?.trim() || "Draft",
        family_kind: family.familyKind?.trim() || null,
        source_model_path: family.sourceModelPath?.trim() || null,
        source_element_type_id: Number.isInteger(family.sourceElementTypeId)
          ? family.sourceElementTypeId
          : null,
        source_discipline: family.sourceDiscipline?.trim() || null
      };

      const { data: upsertRow, error: upsertError } = await supabase
        .schema("app")
        .from("families")
        .upsert(familyPayload, { onConflict: "rfa_path" })
        .select("family_id")
        .single();

      if (upsertError || !upsertRow?.family_id) {
        failed++;
        errors.push(
          `Item ${i}: ${upsertError?.message ?? "Failed to upsert family"}`
        );
        continue;
      }

      const familyId = upsertRow.family_id as number;
      const { error: deleteParamsError } = await supabase
        .schema("app")
        .from("parameters")
        .delete()
        .eq("family_id", familyId);

      if (deleteParamsError) {
        failed++;
        errors.push(`Item ${i}: ${deleteParamsError.message}`);
        continue;
      }

      if (parameters.length > 0) {
        const parameterPayload = parameters
          .filter((p) => (p.parameterName?.trim() ?? "").length > 0)
          .map((p) => ({
            family_id: familyId,
            parameter_name: p.parameterName!.trim(),
            parameter_group_name: p.parameterGroupName?.trim() || null,
            storage_type: p.storageType?.trim() || null,
            unit_type: p.unitType?.trim() || null,
            is_instance: Boolean(p.isInstance),
            is_shared: Boolean(p.isShared),
            string_value: p.stringValue ?? null,
            number_value: p.numberValue ?? null,
            integer_value: p.integerValue ?? null,
            element_id_value: p.elementIdValue ?? null
          }));

        if (parameterPayload.length > 0) {
          const { error: paramsInsertError } = await supabase
            .schema("app")
            .from("parameters")
            .insert(parameterPayload);

          if (paramsInsertError) {
            failed++;
            errors.push(`Item ${i}: ${paramsInsertError.message}`);
            continue;
          }
        }
      }

      upserted++;
    }

    return NextResponse.json({
      ok: failed === 0,
      upserted,
      failed,
      total: items.length,
      errors: errors.slice(0, 20)
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
