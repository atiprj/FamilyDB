import Link from "next/link";
import { getSupabaseAdminClient } from "@/lib/supabase";

async function getHealth() {
  try {
    const supabase = getSupabaseAdminClient();
    const { count, error } = await supabase
      .schema("app")
      .from("families")
      .select("*", { count: "exact", head: true });

    if (error) {
      return { ok: false, error: error.message };
    }

    return {
      ok: true,
      familiesCount: count ?? 0,
      checkedAtUtc: new Date().toISOString()
    };
  } catch (error) {
    return {
      ok: false,
      error: error instanceof Error ? error.message : "Unknown error"
    };
  }
}

export default async function HomePage() {
  const health = await getHealth();

  return (
    <main style={{ maxWidth: 920, margin: "0 auto", padding: "42px 20px" }}>
      <h1 style={{ marginTop: 0, fontSize: 34 }}>FamilyDB Online Viewer</h1>
      <p style={{ color: "#cbd5e1", marginTop: 0 }}>
        Base web su Vercel collegata a Supabase.
      </p>

      <section
        style={{
          border: "1px solid #2b3844",
          borderRadius: 14,
          padding: 18,
          background: "#111b24"
        }}
      >
        <h2 style={{ marginTop: 0, fontSize: 20 }}>Supabase Health</h2>
        {health.ok ? (
          <>
            <p style={{ marginBottom: 8 }}>
              Stato: <b style={{ color: "#22c55e" }}>OK</b>
            </p>
            <p style={{ margin: "4px 0" }}>
              Famiglie presenti in `app.families`:{" "}
              <b>{health.familiesCount ?? 0}</b>
            </p>
            <p style={{ margin: "4px 0", color: "#94a3b8" }}>
              Ultimo check: {health.checkedAtUtc}
            </p>
          </>
        ) : (
          <>
            <p style={{ marginBottom: 8 }}>
              Stato: <b style={{ color: "#f97316" }}>NON PRONTO</b>
            </p>
            <p style={{ margin: "4px 0" }}>
              Errore: <code>{health.error}</code>
            </p>
            <p style={{ margin: "10px 0 0", color: "#94a3b8" }}>
              Verifica le env vars su Vercel e l&apos;esecuzione dello script SQL in Supabase.
            </p>
          </>
        )}
      </section>

      <section style={{ marginTop: 18 }}>
        <Link
          href="/catalog"
          style={{
            display: "inline-block",
            padding: "12px 20px",
            borderRadius: 10,
            background: "#2563eb",
            color: "#fff",
            textDecoration: "none",
            fontWeight: 600
          }}
        >
          Apri catalogo famiglie →
        </Link>
        <p style={{ margin: "12px 0 0", color: "#94a3b8", fontSize: 14 }}>
          In Revit: tab FamCloud → Sync ALL → Cloud per aggiornare il catalogo libreria ARC/FUR.
        </p>
      </section>
    </main>
  );
}
