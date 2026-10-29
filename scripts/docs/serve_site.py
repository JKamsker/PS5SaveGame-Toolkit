"""Preview a static docs build at both / and the GitHub Pages project base, on loopback only."""

import argparse
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

PROJECT_BASE = "/PS5SaveGame-Toolkit"


class DocsHandler(SimpleHTTPRequestHandler):
    def translate_path(self, path):
        # No copied directory, symlink or junction is needed to exercise a project site.
        if path == PROJECT_BASE or path.startswith(PROJECT_BASE + "/"):
            path = path[len(PROJECT_BASE):] or "/"
        return super().translate_path(path)

    def log_message(self, format, *args):
        pass


def create_server(site, port=0):
    return ThreadingHTTPServer(("127.0.0.1", port), partial(DocsHandler, directory=str(site)))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--site", required=True, type=Path)
    parser.add_argument("--port", type=int, default=8080)
    args = parser.parse_args()
    if not (args.site / "index.html").is_file():
        parser.error("--site must point to a built documentation directory")
    with create_server(args.site, args.port) as server:
        print(f"Root preview: http://127.0.0.1:{server.server_port}/", flush=True)
        print(f"Pages preview: http://127.0.0.1:{server.server_port}{PROJECT_BASE}/", flush=True)
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass
