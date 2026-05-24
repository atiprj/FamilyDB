import Link from "next/link";
import type { CSSProperties } from "react";
import { CatalogTable } from "@/components/CatalogTable";
import { fetchCatalogFamilies } from "@/lib/catalog";

export const dynamic = "force-dynamic";

type SearchParams = Promise<{
  q?: string;
  discipline?: string;
  kind?: string;
}>;

export default async function CatalogPage({
  searchParams
}: {
  searchParams: SearchParams;
}) {
  const params = await searchParams;
  const q = params.q?.trim() ?? "";
  const discipline = params.discipline?.trim() ?? "";
  const kind = params.kind?.trim() ?? "";

  const { items, error, totalLoaded } = await fetchCatalogFamilies({
    q,
    discipline,
    kind
  });

  return (
    <main style={{ maxWidth: 1280, margin: "0 auto", padding: "32px 20px" }}>
      <p style={{ margin: "0 0 8px" }}>
        <Link href="/" style={{ color: "#60a5fa" }}>
          ← Home
        </Link>
        {" · "}
        <Link href="/report" style={{ color: "#60a5fa" }}>
          Report
        </Link>
      </p>
      <h1 style={{ marginTop: 0, fontSize: 30 }}>Catalogo famiglie</h1>
      <p style={{ color: "#94a3b8", marginTop: 0 }}>
        Loadable e System da Supabase. Sync: FamCloud → Sync ALL → Cloud.
      </p>

      <form
        method="get"
        style={{
          display: "flex",
          flexWrap: "wrap",
          gap: 10,
          margin: "20px 0",
          alignItems: "center"
        }}
      >
        <input
          name="q"
          defaultValue={q}
          placeholder="Cerca nome, categoria, path..."
          style={fieldStyle}
        />
        <select name="discipline" defaultValue={discipline} style={fieldStyle}>
          <option value="">Tutte le discipline</option>
          <option value="ARC">ARC</option>
          <option value="FUR">FUR</option>
        </select>
        <select name="kind" defaultValue={kind} style={fieldStyle}>
          <option value="">Tutti i tipi</option>
          <option value="Loadable">Loadable</option>
          <option value="System">System</option>
        </select>
        <button type="submit" style={buttonStyle}>
          Filtra
        </button>
      </form>

      {error ? (
        <p style={{ color: "#f97316" }}>Errore: {error}</p>
      ) : (
        <p style={{ color: "#cbd5e1", marginBottom: 12 }}>
          Visualizzate: <b>{items.length}</b>
          {totalLoaded !== items.length ? ` (caricate ${totalLoaded})` : null}
        </p>
      )}

      <CatalogTable items={items} />
    </main>
  );
}

const fieldStyle: CSSProperties = {
  padding: "8px 12px",
  borderRadius: 10,
  border: "1px solid #334155",
  background: "#0f172a",
  color: "#f1f5f9",
  minWidth: 180
};

const buttonStyle: CSSProperties = {
  padding: "8px 16px",
  borderRadius: 10,
  border: 0,
  background: "#2563eb",
  color: "#fff",
  cursor: "pointer",
  fontWeight: 600
};
