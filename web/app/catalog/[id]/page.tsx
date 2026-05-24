import Link from "next/link";
import type { CSSProperties } from "react";
import { fetchFamilyDetail } from "@/lib/catalog";
import { isPreviewUrl } from "@/lib/preview";

type Params = Promise<{ id: string }>;

export default async function FamilyDetailPage({ params }: { params: Params }) {
  const { id } = await params;
  const familyId = Number.parseInt(id, 10);

  if (Number.isNaN(familyId) || familyId <= 0) {
    return (
      <main style={{ padding: 32 }}>
        <p>ID famiglia non valido.</p>
      </main>
    );
  }

  const { family, parameters, error } = await fetchFamilyDetail(familyId);

  if (error || !family) {
    return (
      <main style={{ padding: 32 }}>
        <Link href="/catalog" style={{ color: "#60a5fa" }}>
          ← Catalogo
        </Link>
        <p style={{ color: "#f97316", marginTop: 16 }}>
          {error ?? "Famiglia non trovata."}
        </p>
      </main>
    );
  }

  return (
    <main style={{ maxWidth: 960, margin: "0 auto", padding: "32px 20px" }}>
      <p style={{ margin: "0 0 8px" }}>
        <Link href="/catalog" style={{ color: "#60a5fa" }}>
          ← Catalogo
        </Link>
      </p>
      <h1 style={{ marginTop: 0 }}>{family.family_name}</h1>
      <p style={{ color: "#94a3b8" }}>
        {family.category_name ?? "—"} · {family.source_discipline ?? "—"} ·{" "}
        {family.family_kind ?? "—"}
      </p>

      {isPreviewUrl(family.preview_path) ? (
        <div style={{ marginTop: 16 }}>
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img
            src={family.preview_path}
            alt={family.family_name}
            width={160}
            height={160}
            style={{
              objectFit: "cover",
              borderRadius: 10,
              border: "1px solid #334155",
              background: "#fff"
            }}
          />
        </div>
      ) : null}

      <section
        style={{
          marginTop: 20,
          padding: 16,
          border: "1px solid #2b3844",
          borderRadius: 12,
          background: "#111b24"
        }}
      >
        <h2 style={{ marginTop: 0, fontSize: 18 }}>Metadati</h2>
        <dl style={{ display: "grid", gridTemplateColumns: "160px 1fr", gap: "8px 12px", margin: 0 }}>
          <dt style={dtStyle}>RFA path</dt>
          <dd style={ddStyle}>{family.rfa_path}</dd>
          <dt style={dtStyle}>Stato</dt>
          <dd style={ddStyle}>{family.approval_status}</dd>
          <dt style={dtStyle}>Revit</dt>
          <dd style={ddStyle}>{family.revit_version ?? "—"}</dd>
          <dt style={dtStyle}>Modello sorgente</dt>
          <dd style={ddStyle}>{family.source_model_path ?? "—"}</dd>
          <dt style={dtStyle}>Hash</dt>
          <dd style={ddStyle}>{family.file_hash ?? "—"}</dd>
        </dl>
      </section>

      <section style={{ marginTop: 24 }}>
        <h2 style={{ fontSize: 18 }}>Parametri ({parameters.length})</h2>
        <div style={{ overflowX: "auto", border: "1px solid #2b3844", borderRadius: 12 }}>
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 13 }}>
            <thead>
              <tr style={{ background: "#1e293b", textAlign: "left" }}>
                <th style={thStyle}>Nome</th>
                <th style={thStyle}>Gruppo</th>
                <th style={thStyle}>Tipo</th>
                <th style={thStyle}>Valore</th>
              </tr>
            </thead>
            <tbody>
              {parameters.map((p, idx) => (
                <tr key={`${p.parameter_name}-${idx}`} style={{ borderTop: "1px solid #2b3844" }}>
                  <td style={tdStyle}>{p.parameter_name}</td>
                  <td style={tdStyle}>{p.parameter_group_name ?? "—"}</td>
                  <td style={tdStyle}>{p.storage_type ?? "—"}</td>
                  <td style={tdStyle}>
                    {p.string_value ??
                      (p.number_value != null ? String(p.number_value) : null) ??
                      (p.integer_value != null ? String(p.integer_value) : "—")}
                  </td>
                </tr>
              ))}
              {parameters.length === 0 ? (
                <tr>
                  <td colSpan={4} style={{ ...tdStyle, color: "#94a3b8" }}>
                    Nessun parametro salvato (sync con parametri troncati o saltati).
                  </td>
                </tr>
              ) : null}
            </tbody>
          </table>
        </div>
      </section>
    </main>
  );
}

const dtStyle: CSSProperties = { color: "#94a3b8", margin: 0 };
const ddStyle: CSSProperties = { margin: 0, wordBreak: "break-all" };
const thStyle: CSSProperties = { padding: "10px 12px" };
const tdStyle: CSSProperties = { padding: "8px 12px", verticalAlign: "top" };
