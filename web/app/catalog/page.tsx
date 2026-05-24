import Link from "next/link";
import type { CSSProperties } from "react";
import { fetchCatalogFamilies } from "@/lib/catalog";

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

  const { items, error } = await fetchCatalogFamilies({
    q,
    discipline,
    kind,
    take: 500
  });

  return (
    <main style={{ maxWidth: 1200, margin: "0 auto", padding: "32px 20px" }}>
      <p style={{ margin: "0 0 8px" }}>
        <Link href="/" style={{ color: "#60a5fa" }}>
          ← Home
        </Link>
      </p>
      <h1 style={{ marginTop: 0, fontSize: 30 }}>Catalogo famiglie</h1>
      <p style={{ color: "#94a3b8", marginTop: 0 }}>
        Dati da Supabase (`app.families`). Sincronizza da Revit con FamCloud → Sync ARC/FUR/ALL.
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
          Risultati: <b>{items.length}</b>
        </p>
      )}

      <div style={{ overflowX: "auto", border: "1px solid #2b3844", borderRadius: 12 }}>
        <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 13 }}>
          <thead>
            <tr style={{ background: "#1e293b", textAlign: "left" }}>
              <th style={thStyle}>Nome</th>
              <th style={thStyle}>Categoria</th>
              <th style={thStyle}>Disc.</th>
              <th style={thStyle}>Tipo</th>
              <th style={thStyle}>Stato</th>
              <th style={thStyle}>Aggiornato</th>
            </tr>
          </thead>
          <tbody>
            {items.map((row) => (
              <tr key={row.family_id} style={{ borderTop: "1px solid #2b3844" }}>
                <td style={tdStyle}>
                  <Link
                    href={`/catalog/${row.family_id}`}
                    style={{ color: "#93c5fd", textDecoration: "none" }}
                  >
                    {row.family_name}
                  </Link>
                </td>
                <td style={tdStyle}>{row.category_name ?? "—"}</td>
                <td style={tdStyle}>{row.source_discipline ?? "—"}</td>
                <td style={tdStyle}>{row.family_kind ?? "—"}</td>
                <td style={tdStyle}>{row.approval_status}</td>
                <td style={tdStyle}>
                  {row.updated_at_utc
                    ? new Date(row.updated_at_utc).toLocaleString("it-IT")
                    : "—"}
                </td>
              </tr>
            ))}
            {items.length === 0 && !error ? (
              <tr>
                <td colSpan={6} style={{ ...tdStyle, color: "#94a3b8" }}>
                  Nessuna famiglia. In Revit usa FamCloud → Sync ALL → Cloud.
                </td>
              </tr>
            ) : null}
          </tbody>
        </table>
      </div>
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

const thStyle: CSSProperties = { padding: "10px 12px" };
const tdStyle: CSSProperties = { padding: "8px 12px", verticalAlign: "top" };
