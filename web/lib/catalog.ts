import { getSupabaseAdminClient } from "@/lib/supabase";

export type CatalogFamilyRow = {
  family_id: number;
  family_name: string;
  category_name: string | null;
  rfa_path: string;
  preview_path: string | null;
  family_kind: string | null;
  approval_status: string;
  source_discipline: string | null;
  updated_at_utc: string;
};

export type CatalogQuery = {
  q?: string;
  discipline?: string;
  kind?: string;
  category?: string;
  take?: number;
};

export async function fetchCatalogFamilies(query: CatalogQuery) {
  const take = Math.min(Math.max(query.take ?? 300, 1), 1000);
  const supabase = getSupabaseAdminClient();

  let dbQuery = supabase
    .schema("app")
    .from("families")
    .select(
      "family_id,family_name,category_name,rfa_path,preview_path,family_kind,approval_status,source_discipline,updated_at_utc"
    )
    .order("updated_at_utc", { ascending: false })
    .limit(take);

  if (query.discipline?.trim()) {
    dbQuery = dbQuery.eq("source_discipline", query.discipline.trim());
  }
  if (query.kind?.trim()) {
    dbQuery = dbQuery.eq("family_kind", query.kind.trim());
  }
  if (query.category?.trim()) {
    dbQuery = dbQuery.eq("category_name", query.category.trim());
  }
  if (query.q?.trim()) {
    const q = query.q.trim();
    dbQuery = dbQuery.or(
      `family_name.ilike.%${q}%,category_name.ilike.%${q}%,rfa_path.ilike.%${q}%`
    );
  }

  const { data, error } = await dbQuery;
  return {
    items: (data ?? []) as CatalogFamilyRow[],
    error: error?.message ?? null
  };
}

export async function fetchFamilyDetail(familyId: number) {
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
        .select(
          "parameter_name,parameter_group_name,storage_type,string_value,number_value,integer_value"
        )
        .eq("family_id", familyId)
        .order("parameter_name", { ascending: true })
        .limit(500)
    ]);

  return {
    family,
    parameters: parameters ?? [],
    error: familyError?.message ?? paramsError?.message ?? null
  };
}
