import { getSupabaseAdminClient } from "@/lib/supabase";
import { isExcludedCategoryName } from "@/lib/excluded-categories";

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
  maxRows?: number;
};

export type CatalogStats = {
  total: number;
  byCategory: { name: string; count: number }[];
  byDiscipline: { name: string; count: number }[];
  byKind: { name: string; count: number }[];
  withPreview: number;
};

const PAGE_SIZE = 1000;
const DEFAULT_MAX_ROWS = 50_000;

function applyCatalogFilters(
  supabase: ReturnType<typeof getSupabaseAdminClient>,
  filters: CatalogQuery
) {
  let dbQuery = supabase
    .schema("app")
    .from("families")
    .select(
      "family_id,family_name,category_name,rfa_path,preview_path,family_kind,approval_status,source_discipline,updated_at_utc"
    )
    .order("family_name", { ascending: true });

  if (filters.discipline?.trim()) {
    dbQuery = dbQuery.eq("source_discipline", filters.discipline.trim());
  }
  if (filters.kind?.trim()) {
    dbQuery = dbQuery.eq("family_kind", filters.kind.trim());
  }
  if (filters.category?.trim()) {
    dbQuery = dbQuery.eq("category_name", filters.category.trim());
  }
  if (filters.q?.trim()) {
    const q = filters.q.trim();
    dbQuery = dbQuery.or(
      `family_name.ilike.%${q}%,category_name.ilike.%${q}%,rfa_path.ilike.%${q}%`
    );
  }

  return dbQuery;
}

/** Carica tutte le righe (paginazione Supabase) fino a maxRows. */
export async function fetchCatalogFamilies(query: CatalogQuery = {}) {
  const maxRows = Math.min(query.maxRows ?? DEFAULT_MAX_ROWS, DEFAULT_MAX_ROWS);
  const supabase = getSupabaseAdminClient();
  const items: CatalogFamilyRow[] = [];
  let from = 0;

  while (items.length < maxRows) {
    const to = Math.min(from + PAGE_SIZE - 1, maxRows - 1);
    let dbQuery = applyCatalogFilters(supabase, query).range(from, to);
    const { data, error } = await dbQuery;

    if (error) {
      return { items, error: error.message, totalLoaded: items.length };
    }

    const page = ((data ?? []) as CatalogFamilyRow[]).filter(
      (row) => !isExcludedCategoryName(row.category_name)
    );
    items.push(...page);

    if (page.length < PAGE_SIZE) {
      break;
    }

    from += PAGE_SIZE;
  }

  return { items, error: null as string | null, totalLoaded: items.length };
}

export async function fetchCatalogStats(): Promise<{
  stats: CatalogStats | null;
  error: string | null;
}> {
  const supabase = getSupabaseAdminClient();
  const rows: {
    category_name: string | null;
    source_discipline: string | null;
    family_kind: string | null;
    preview_path: string | null;
  }[] = [];

  let from = 0;
  while (from < DEFAULT_MAX_ROWS) {
    const to = from + PAGE_SIZE - 1;
    const { data, error } = await supabase
      .schema("app")
      .from("families")
      .select("category_name,source_discipline,family_kind,preview_path")
      .range(from, to);

    if (error) {
      return { stats: null, error: error.message };
    }

    const page = data ?? [];
    rows.push(...page);
    if (page.length < PAGE_SIZE) {
      break;
    }

    from += PAGE_SIZE;
  }

  const categoryMap = new Map<string, number>();
  const disciplineMap = new Map<string, number>();
  const kindMap = new Map<string, number>();
  let withPreview = 0;
  let total = 0;

  for (const row of rows) {
    if (isExcludedCategoryName(row.category_name)) {
      continue;
    }

    total++;
    const cat = row.category_name?.trim() || "Senza categoria";
    categoryMap.set(cat, (categoryMap.get(cat) ?? 0) + 1);

    const disc = row.source_discipline?.trim() || "N/D";
    disciplineMap.set(disc, (disciplineMap.get(disc) ?? 0) + 1);

    const kind = row.family_kind?.trim() || "N/D";
    kindMap.set(kind, (kindMap.get(kind) ?? 0) + 1);

    if (row.preview_path?.startsWith("http")) {
      withPreview += 1;
    }
  }

  const sortDesc = (a: { count: number }, b: { count: number }) => b.count - a.count;

  return {
    stats: {
      total,
      byCategory: [...categoryMap.entries()]
        .map(([name, count]) => ({ name, count }))
        .sort(sortDesc),
      byDiscipline: [...disciplineMap.entries()]
        .map(([name, count]) => ({ name, count }))
        .sort(sortDesc),
      byKind: [...kindMap.entries()]
        .map(([name, count]) => ({ name, count }))
        .sort(sortDesc),
      withPreview
    },
    error: null
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

export { isPreviewUrl } from "@/lib/preview";
