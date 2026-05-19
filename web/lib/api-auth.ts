import { NextResponse } from "next/server";

export function requireAddinApiKey(request: Request) {
  const configuredApiKey = process.env.ADDIN_API_KEY?.trim();
  if (!configuredApiKey) {
    return null;
  }

  const bearer = request.headers.get("authorization");
  const headerKey = request.headers.get("x-api-key");
  const token = bearer?.startsWith("Bearer ") ? bearer.slice(7).trim() : "";
  const provided = token || (headerKey ?? "").trim();

  if (!provided || provided !== configuredApiKey) {
    return NextResponse.json(
      { ok: false, error: "Unauthorized" },
      { status: 401 }
    );
  }

  return null;
}
