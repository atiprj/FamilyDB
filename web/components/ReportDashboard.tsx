"use client";

import Link from "next/link";
import { useMemo, useState } from "react";
import type { CatalogFamilyRow, CatalogStats } from "@/lib/catalog";

const palette = [
  "#3b82f6",
  "#22c55e",
  "#f59e0b",
  "#ef4444",
  "#a855f7",
  "#06b6d4",
  "#ec4899",
  "#84cc16",
  "#f97316",
  "#64748b"
];

type ChartFilter =
  | { type: "discipline"; name: string }
  | { type: "category"; name: string }
  | null;

function DonutChart({
  slices,
  size = 220,
  onSliceClick
}: {
  slices: { label: string; value: number; color: string }[];
  size?: number;
  onSliceClick?: (label: string) => void;
}) {
  const total = slices.reduce((s, x) => s + x.value, 0) || 1;
  const r = size / 2 - 8;
  const cx = size / 2;
  const cy = size / 2;
  let angle = -Math.PI / 2;
  const paths: { d: string; color: string; label: string }[] = [];

  for (const slice of slices) {
    const sweep = (slice.value / total) * Math.PI * 2;
    const x1 = cx + r * Math.cos(angle);
    const y1 = cy + r * Math.sin(angle);
    angle += sweep;
    const x2 = cx + r * Math.cos(angle);
    const y2 = cy + r * Math.sin(angle);
    const large = sweep > Math.PI ? 1 : 0;
    paths.push({
      d: `M ${cx} ${cy} L ${x1} ${y1} A ${r} ${r} 0 ${large} 1 ${x2} ${y2} Z`,
      color: slice.color,
      label: slice.label
    });
  }

  return (
    <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`}>
      {paths.map((p, i) => (
        <path
          key={i}
          d={p.d}
          fill={p.color}
          stroke="#0f172a"
          strokeWidth={1}
          style={{ cursor: onSliceClick ? "pointer" : undefined }}
          onClick={() => onSliceClick?.(p.label)}
        />
      ))}
      <circle cx={cx} cy={cy} r={r * 0.55} fill="#111b24" />
      <text
        x={cx}
        y={cy}
        textAnchor="middle"
        dominantBaseline="middle"
        fill="#f1f5f9"
        fontSize={18}
        fontWeight={700}
      >
        {total}
      </text>
    </svg>
  );
}

function ScrollableBarChart({
  rows,
  activeName,
  onSelect
}: {
  rows: { name: string; count: number }[];
  activeName: string | null;
  onSelect: (name: string) => void;
}) {
  const max = Math.max(...rows.map((r) => r.count), 1);

  return (
    <div
      style={{
        maxHeight: 420,
        overflowY: "auto",
        paddingRight: 6
      }}
    >
      <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
        {rows.map((row, i) => {
          const selected = activeName === row.name;
          return (
            <button
              key={row.name}
              type="button"
              onClick={() => onSelect(row.name)}
              style={{
                display: "block",
                width: "100%",
                border: selected ? "1px solid #60a5fa" : "1px solid transparent",
                borderRadius: 8,
                background: selected ? "#1e3a5f" : "transparent",
                padding: "6px 8px",
                cursor: "pointer",
                textAlign: "left"
              }}
            >
              <div
                style={{
                  display: "flex",
                  justifyContent: "space-between",
                  fontSize: 12,
                  color: "#cbd5e1",
                  marginBottom: 4
                }}
              >
                <span title={row.name}>{row.name}</span>
                <span>{row.count}</span>
              </div>
              <div
                style={{
                  height: 10,
                  borderRadius: 6,
                  background: "#1e293b",
                  overflow: "hidden"
                }}
              >
                <div
                  style={{
                    width: `${(row.count / max) * 100}%`,
                    height: "100%",
                    background: palette[i % palette.length],
                    borderRadius: 6
                  }}
                />
              </div>
            </button>
          );
        })}
      </div>
    </div>
  );
}

export function ReportDashboard({
  stats,
  families
}: {
  stats: CatalogStats;
  families: CatalogFamilyRow[];
}) {
  const [filter, setFilter] = useState<ChartFilter>(null);

  const toggleFilter = (next: ChartFilter) => {
    setFilter((current) => {
      if (
        current?.type === next?.type &&
        current?.name === next?.name
      ) {
        return null;
      }
      return next;
    });
  };

  const filteredFamilies = useMemo(() => {
    if (!filter) {
      return families;
    }

    if (filter.type === "discipline") {
      if (filter.name === "N/D") {
        return families.filter((f) => !f.source_discipline?.trim());
      }
      return families.filter(
        (f) =>
          (f.source_discipline?.trim() || "") === filter.name
      );
    }

    if (filter.name === "Senza categoria") {
      return families.filter((f) => !f.category_name?.trim());
    }

    return families.filter(
      (f) => (f.category_name?.trim() || "") === filter.name
    );
  }, [families, filter]);

  const topCategories = stats.byCategory.slice(0, 8);
  const donutSlices = topCategories.map((row, i) => ({
    label: row.name,
    value: row.count,
    color: palette[i % palette.length]
  }));
  const otherCount =
    stats.total - topCategories.reduce((s, r) => s + r.count, 0);
  if (otherCount > 0) {
    donutSlices.push({
      label: "Altre",
      value: otherCount,
      color: "#475569"
    });
  }

  return (
    <>
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
          gap: 20,
          marginTop: 20
        }}
      >
        <section style={panelStyle}>
          <h2 style={{ marginTop: 0, fontSize: 18 }}>Distribuzione per disciplina</h2>
          <p style={{ color: "#94a3b8", fontSize: 12, marginTop: 0 }}>
            Clic su fetta o legenda per filtrare l&apos;elenco sotto.
          </p>
          <div style={{ display: "flex", gap: 16, alignItems: "flex-start", flexWrap: "wrap" }}>
            <DonutChart
              slices={stats.byDiscipline.map((row, i) => ({
                label: row.name,
                value: row.count,
                color: palette[i % palette.length]
              }))}
              onSliceClick={(name) =>
                toggleFilter({ type: "discipline", name })
              }
            />
            <ul style={{ margin: 0, padding: 0, listStyle: "none", fontSize: 13 }}>
              {stats.byDiscipline.map((row, i) => {
                const active =
                  filter?.type === "discipline" && filter.name === row.name;
                return (
                  <li key={row.name} style={{ marginBottom: 6 }}>
                    <button
                      type="button"
                      onClick={() =>
                        toggleFilter({ type: "discipline", name: row.name })
                      }
                      style={{
                        border: 0,
                        background: active ? "#1e3a5f" : "transparent",
                        color: "#cbd5e1",
                        cursor: "pointer",
                        padding: "4px 6px",
                        borderRadius: 6,
                        width: "100%",
                        textAlign: "left"
                      }}
                    >
                      <span
                        style={{
                          display: "inline-block",
                          width: 10,
                          height: 10,
                          borderRadius: 2,
                          background: palette[i % palette.length],
                          marginRight: 8
                        }}
                      />
                      {row.name}: <b>{row.count}</b>
                    </button>
                  </li>
                );
              })}
            </ul>
          </div>
        </section>

        <section style={panelStyle}>
          <h2 style={{ marginTop: 0, fontSize: 18 }}>Categorie</h2>
          <p style={{ color: "#94a3b8", fontSize: 12, marginTop: 0 }}>
            Elenco scrollabile — clic su una barra per filtrare le famiglie.
          </p>
          <ScrollableBarChart
            rows={stats.byCategory}
            activeName={
              filter?.type === "category" ? filter.name : null
            }
            onSelect={(name) => toggleFilter({ type: "category", name })}
          />
        </section>
      </div>

      <section style={{ ...panelStyle, marginTop: 20 }}>
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            flexWrap: "wrap",
            gap: 10,
            marginBottom: 10
          }}
        >
          <h2 style={{ margin: 0, fontSize: 18 }}>
            Famiglie
            {filter ? (
              <span style={{ color: "#94a3b8", fontWeight: 400, fontSize: 14 }}>
                {" "}
                — {filter.type === "discipline" ? "Disciplina" : "Categoria"}:{" "}
                <b style={{ color: "#e2e8f0" }}>{filter.name}</b>
              </span>
            ) : null}
          </h2>
          <div style={{ display: "flex", gap: 8, alignItems: "center" }}>
            <span style={{ color: "#94a3b8", fontSize: 13 }}>
              {filteredFamilies.length} voci
            </span>
            {filter ? (
              <button type="button" onClick={() => setFilter(null)} style={clearBtnStyle}>
                Azzera filtro
              </button>
            ) : null}
          </div>
        </div>

        <div
          style={{
            maxHeight: "min(480px, 50vh)",
            overflowY: "auto",
            border: "1px solid #2b3844",
            borderRadius: 10
          }}
        >
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 13 }}>
            <thead style={{ position: "sticky", top: 0, zIndex: 1 }}>
              <tr style={{ background: "#1e293b", textAlign: "left" }}>
                <th style={thStyle}>Nome</th>
                <th style={thStyle}>Categoria</th>
                <th style={thStyle}>Disc.</th>
                <th style={thStyle}>Tipo</th>
                <th style={thStyle}></th>
              </tr>
            </thead>
            <tbody>
              {filteredFamilies.map((row) => (
                <tr key={row.family_id} style={{ borderTop: "1px solid #2b3844" }}>
                  <td style={{ ...tdStyle, color: "#e2e8f0" }}>{row.family_name}</td>
                  <td style={tdStyle}>{row.category_name ?? "—"}</td>
                  <td style={tdStyle}>{row.source_discipline ?? "—"}</td>
                  <td style={tdStyle}>{row.family_kind ?? "—"}</td>
                  <td style={tdStyle}>
                    <Link
                      href={`/catalog/${row.family_id}`}
                      style={{ color: "#60a5fa", fontSize: 12 }}
                    >
                      Dettaglio
                    </Link>
                  </td>
                </tr>
              ))}
              {filteredFamilies.length === 0 ? (
                <tr>
                  <td colSpan={5} style={{ ...tdStyle, color: "#94a3b8" }}>
                    Nessuna famiglia per il filtro selezionato.
                  </td>
                </tr>
              ) : null}
            </tbody>
          </table>
        </div>
      </section>
    </>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #2b3844",
  borderRadius: 14,
  padding: 18,
  background: "#111b24"
};

const thStyle: React.CSSProperties = { padding: "10px 12px" };
const tdStyle: React.CSSProperties = { padding: "8px 12px", verticalAlign: "middle" };

const clearBtnStyle: React.CSSProperties = {
  padding: "6px 12px",
  borderRadius: 8,
  border: "1px solid #475569",
  background: "#1e293b",
  color: "#f1f5f9",
  cursor: "pointer",
  fontSize: 12
};
