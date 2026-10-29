import { useQuery } from "@tanstack/react-query";

async function loadVersion(signal: AbortSignal): Promise<string> {
  const response = await fetch("/api/version", { signal });
  if (!response.ok) throw new Error("Version unavailable");
  const body: unknown = await response.json();
  if (!body || typeof body !== "object" || !("version" in body) || typeof body.version !== "string") {
    throw new Error("Invalid version response");
  }
  return body.version;
}

export function AppVersion() {
  const version = useQuery({
    queryKey: ["appVersion"], queryFn: ({ signal }) => loadVersion(signal),
    staleTime: Infinity, retry: false,
  });
  const label = version.isPending ? "Loading version…" : version.data ? `v${version.data}` : "Version unavailable";
  return <footer className="app-version" aria-label="Application version">
    PS5 Save Game Toolkit · {label}
  </footer>;
}
