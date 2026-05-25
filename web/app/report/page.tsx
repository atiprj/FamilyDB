import Link from "next/link";
import { ReportDashboard } from "@/components/ReportDashboard";
import { fetchCatalogFamilies, fetchCatalogStats } from "@/lib/catalog";

export const dynamic = "force-dynamic";

export default async function ReportPage() {
  const [{ stats, error }, { items: families, error: familiesError }] =
    await Promise.all([fetchCatalogStats(), fetchCatalogFamilies()]);

  return (
    <main style={{ maxWidth: 1100, margin: "0 auto", padding: "32px 20px" }}>
      <p style={{ margin: "0 0 8px" }}>
        <Link href="/" style={{ color: "#60a5fa" }}>
          ← Home
        </Link>
        {" · "}
        <Link href="/catalog" style={{ color: "#60a5fa" }}>
          Catalogo
        </Link>
      </p>
      <h1 style={{ marginTop: 0, fontSize: 30 }}>Report catalogo</h1>
      <p style={{ color: "#94a3b8", marginTop: 0 }}>
        Statistiche aggregate da Supabase (`app.families`).
      </p>

      {error || familiesError ? (
        <p style={{ color: "#f97316" }}>
          Errore: {error ?? familiesError}
        </p>
      ) : stats ? (
        <>
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(140px, 1fr))",
              gap: 12,
              marginTop: 16
            }}
          >
            <StatCard label="Totale famiglie" value={stats.total} />
            <StatCard label="Con anteprima" value={stats.withPreview} />
            <StatCard label="Categorie" value={stats.byCategory.length} />
            <StatCard label="Loadable" value={stats.byKind.find((k) => k.name === "Loadable")?.count ?? 0} />
            <StatCard label="System" value={stats.byKind.find((k) => k.name === "System")?.count ?? 0} />
          </div>

          <ReportDashboard stats={stats} families={families} />
        </>
      ) : null}
    </main>
  );
}

function StatCard({ label, value }: { label: string; value: number }) {
  return (
    <div
      style={{
        border: "1px solid #2b3844",
        borderRadius: 12,
        padding: "14px 16px",
        background: "#111b24"
      }}
    >
      <div style={{ fontSize: 12, color: "#94a3b8" }}>{label}</div>
      <div style={{ fontSize: 26, fontWeight: 700, marginTop: 4 }}>{value}</div>
    </div>
  );
}
