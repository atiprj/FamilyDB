"use client";

import { useCallback, useEffect, useState } from "react";
import type { CatalogFamilyRow } from "@/lib/catalog";
import { isPreviewUrl } from "@/lib/preview";

type FamilyDetail = {
  family_id: number;
  family_name: string;
  category_name: string | null;
  rfa_path: string;
  preview_path: string | null;
  family_kind: string | null;
  approval_status: string;
  source_discipline: string | null;
  source_model_path: string | null;
  revit_version: number | null;
  file_hash: string | null;
};

type ParameterRow = {
  parameter_name: string;
  parameter_group_name: string | null;
  storage_type: string | null;
  string_value: string | null;
  number_value: number | null;
  integer_value: number | null;
};

type Props = {
  items: CatalogFamilyRow[];
};

export function CatalogTable({ items }: Props) {
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [detail, setDetail] = useState<FamilyDetail | null>(null);
  const [parameters, setParameters] = useState<ParameterRow[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const closePanel = useCallback(() => {
    setSelectedId(null);
    setDetail(null);
    setParameters([]);
    setError(null);
  }, []);

  useEffect(() => {
    if (selectedId == null) {
      return;
    }

    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        closePanel();
      }
    };

    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [selectedId, closePanel]);

  const openDetail = async (familyId: number) => {
    setSelectedId(familyId);
    setLoading(true);
    setError(null);
    setDetail(null);
    setParameters([]);

    try {
      const res = await fetch(`/api/family/${familyId}`);
      const data = await res.json();
      if (!res.ok || !data.ok) {
        throw new Error(data.error ?? `HTTP ${res.status}`);
      }

      setDetail(data.family as FamilyDetail);
      setParameters((data.parameters ?? []) as ParameterRow[]);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Errore caricamento dettaglio");
    } finally {
      setLoading(false);
    }
  };

  return (
    <>
      <p style={{ color: "#94a3b8", fontSize: 13, margin: "0 0 10px" }}>
        Doppio clic su una riga per aprire parametri e dettagli (senza cambiare pagina).
      </p>

      <div
        style={{
          border: "1px solid #2b3844",
          borderRadius: 12,
          maxHeight: "calc(100vh - 300px)",
          overflow: "auto"
        }}
      >
        <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 13 }}>
          <thead style={{ position: "sticky", top: 0, zIndex: 1 }}>
            <tr style={{ background: "#1e293b", textAlign: "left" }}>
              <th style={{ ...thStyle, width: 72 }}>Anteprima</th>
              <th style={thStyle}>Nome</th>
              <th style={thStyle}>Categoria</th>
              <th style={thStyle}>Disc.</th>
              <th style={thStyle}>Tipo</th>
              <th style={thStyle}>Stato</th>
            </tr>
          </thead>
          <tbody>
            {items.map((row) => (
              <tr
                key={row.family_id}
                style={{
                  borderTop: "1px solid #2b3844",
                  cursor: "pointer",
                  background: selectedId === row.family_id ? "#1e3a5f" : undefined
                }}
                onDoubleClick={() => openDetail(row.family_id)}
                title="Doppio clic: dettaglio parametri"
              >
                <td style={tdStyle}>
                  {isPreviewUrl(row.preview_path) ? (
                    // eslint-disable-next-line @next/next/no-img-element
                    <img
                      src={row.preview_path!}
                      alt=""
                      width={56}
                      height={56}
                      style={{
                        objectFit: "cover",
                        borderRadius: 6,
                        background: "#fff",
                        display: "block",
                        pointerEvents: "none"
                      }}
                    />
                  ) : (
                    <CategoryThumbPlaceholder
                      label={row.category_name ?? row.family_name}
                      kind={row.family_kind}
                    />
                  )}
                </td>
                <td style={{ ...tdStyle, color: "#e2e8f0" }}>{row.family_name}</td>
                <td style={tdStyle}>{row.category_name ?? "—"}</td>
                <td style={tdStyle}>{row.source_discipline ?? "—"}</td>
                <td style={tdStyle}>{row.family_kind ?? "—"}</td>
                <td style={tdStyle}>{row.approval_status}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {selectedId != null ? (
        <div
          role="presentation"
          onClick={closePanel}
          style={{
            position: "fixed",
            inset: 0,
            background: "rgba(0,0,0,0.55)",
            zIndex: 100,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            padding: 20
          }}
        >
          <div
            role="dialog"
            aria-modal="true"
            onClick={(e) => e.stopPropagation()}
            style={{
              width: "min(920px, 100%)",
              maxHeight: "88vh",
              overflow: "auto",
              background: "#111b24",
              border: "1px solid #334155",
              borderRadius: 14,
              padding: 20,
              boxShadow: "0 16px 48px rgba(0,0,0,0.45)"
            }}
          >
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "flex-start",
                gap: 12,
                marginBottom: 12
              }}
            >
              <div>
                <h2 style={{ margin: 0, fontSize: 20 }}>
                  {detail?.family_name ?? "Caricamento…"}
                </h2>
                {detail ? (
                  <p style={{ margin: "6px 0 0", color: "#94a3b8", fontSize: 13 }}>
                    {detail.category_name ?? "—"} · {detail.source_discipline ?? "—"} ·{" "}
                    {detail.family_kind ?? "—"}
                  </p>
                ) : null}
              </div>
              <button type="button" onClick={closePanel} style={closeBtnStyle}>
                Chiudi
              </button>
            </div>

            {loading ? <p style={{ color: "#94a3b8" }}>Caricamento parametri…</p> : null}
            {error ? <p style={{ color: "#f97316" }}>{error}</p> : null}

            {detail && !loading ? (
              <>
                <div style={{ display: "flex", gap: 16, flexWrap: "wrap", marginBottom: 16 }}>
                  {isPreviewUrl(detail.preview_path) ? (
                    // eslint-disable-next-line @next/next/no-img-element
                    <img
                      src={detail.preview_path!}
                      alt={detail.family_name}
                      width={120}
                      height={120}
                      style={{
                        objectFit: "cover",
                        borderRadius: 8,
                        background: "#fff",
                        border: "1px solid #334155"
                      }}
                    />
                  ) : null}
                  <dl
                    style={{
                      display: "grid",
                      gridTemplateColumns: "120px 1fr",
                      gap: "6px 12px",
                      margin: 0,
                      fontSize: 13,
                      flex: 1,
                      minWidth: 260
                    }}
                  >
                    <dt style={dtStyle}>Stato</dt>
                    <dd style={ddStyle}>{detail.approval_status}</dd>
                    <dt style={dtStyle}>Revit</dt>
                    <dd style={ddStyle}>{detail.revit_version ?? "—"}</dd>
                    <dt style={dtStyle}>RFA path</dt>
                    <dd style={ddStyle}>{detail.rfa_path}</dd>
                  </dl>
                </div>

                <h3 style={{ fontSize: 16, margin: "0 0 8px" }}>
                  Parametri ({parameters.length})
                </h3>
                <div
                  style={{
                    overflowX: "auto",
                    border: "1px solid #2b3844",
                    borderRadius: 10,
                    maxHeight: 360
                  }}
                >
                  <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
                    <thead>
                      <tr style={{ background: "#1e293b" }}>
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
                            Nessun parametro salvato.
                          </td>
                        </tr>
                      ) : null}
                    </tbody>
                  </table>
                </div>
              </>
            ) : null}
          </div>
        </div>
      ) : null}
    </>
  );
}

const thStyle: React.CSSProperties = { padding: "10px 12px", textAlign: "left" };
const tdStyle: React.CSSProperties = { padding: "8px 12px", verticalAlign: "middle" };
const dtStyle: React.CSSProperties = { color: "#94a3b8", margin: 0 };
const ddStyle: React.CSSProperties = { margin: 0, wordBreak: "break-all" };
function CategoryThumbPlaceholder({
  label,
  kind
}: {
  label: string | null;
  kind: string | null;
}) {
  const letter = (label ?? kind ?? "?").trim().charAt(0).toUpperCase() || "?";
  const bg = kind === "System" ? "#334155" : "#1e3a5f";
  return (
    <span
      style={{
        display: "inline-flex",
        width: 56,
        height: 56,
        alignItems: "center",
        justifyContent: "center",
        borderRadius: 6,
        background: bg,
        color: "#e2e8f0",
        fontSize: 18,
        fontWeight: 700
      }}
    >
      {letter}
    </span>
  );
}

const closeBtnStyle: React.CSSProperties = {
  padding: "8px 14px",
  borderRadius: 8,
  border: "1px solid #475569",
  background: "#1e293b",
  color: "#f1f5f9",
  cursor: "pointer",
  fontWeight: 600
};
