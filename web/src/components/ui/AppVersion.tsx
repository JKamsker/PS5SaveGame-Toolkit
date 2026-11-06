import { useQuery } from "@tanstack/react-query";
import { fetchApi } from "../../api/http";

/** Served by the in-browser runtime, so it reports the version of the loaded .NET bundle. */
function loadVersion(signal: AbortSignal): Promise<string> {
  return fetchApi("/version", { signal }, async response => {
    if (!response.ok) throw new Error("Version unavailable");
    const body: unknown = await response.json();
    if (!body || typeof body !== "object" || !("version" in body) || typeof body.version !== "string") {
      throw new Error("Invalid version response");
    }
    return body.version;
  });
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
