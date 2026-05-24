import type { CatalogStats } from "@/lib/catalog";

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

function DonutChart({
  slices,
  size = 220
}: {
  slices: { label: string; value: number; color: string }[];
  size?: number;
}) {
  const total = slices.reduce((s, x) => s + x.value, 0) || 1;
  const r = size / 2 - 8;
  const cx = size / 2;
  const cy = size / 2;
  let angle = -Math.PI / 2;
  const paths: { d: string; color: string }[] = [];

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
      color: slice.color
    });
  }

  return (
    <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`}>
      {paths.map((p, i) => (
        <path key={i} d={p.d} fill={p.color} stroke="#0f172a" strokeWidth={1} />
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

function BarChart({
  rows,
  maxBars = 12
}: {
  rows: { name: string; count: number }[];
  maxBars?: number;
}) {
  const top = rows.slice(0, maxBars);
  const max = Math.max(...top.map((r) => r.count), 1);

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
      {top.map((row, i) => (
        <div key={row.name}>
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              fontSize: 12,
              color: "#cbd5e1",
              marginBottom: 4
            }}
          >
            <span title={row.name}>
              {row.name.length > 36 ? row.name.slice(0, 33) + "…" : row.name}
            </span>
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
        </div>
      ))}
    </div>
  );
}

export function CatalogCharts({ stats }: { stats: CatalogStats }) {
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
    <div
      style={{
        display: "grid",
        gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
        gap: 20,
        marginTop: 20
      }}
    >
      <section
        style={{
          border: "1px solid #2b3844",
          borderRadius: 14,
          padding: 18,
          background: "#111b24"
        }}
      >
        <h2 style={{ marginTop: 0, fontSize: 18 }}>Distribuzione per disciplina</h2>
        <div style={{ display: "flex", gap: 16, alignItems: "center", flexWrap: "wrap" }}>
          <DonutChart
            slices={stats.byDiscipline.map((row, i) => ({
              label: row.name,
              value: row.count,
              color: palette[i % palette.length]
            }))}
          />
          <ul style={{ margin: 0, padding: 0, listStyle: "none", fontSize: 13 }}>
            {stats.byDiscipline.map((row, i) => (
              <li key={row.name} style={{ marginBottom: 6, color: "#cbd5e1" }}>
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
              </li>
            ))}
          </ul>
        </div>
      </section>

      <section
        style={{
          border: "1px solid #2b3844",
          borderRadius: 14,
          padding: 18,
          background: "#111b24"
        }}
      >
        <h2 style={{ marginTop: 0, fontSize: 18 }}>Top categorie</h2>
        <BarChart rows={stats.byCategory} maxBars={14} />
      </section>
    </div>
  );
}
