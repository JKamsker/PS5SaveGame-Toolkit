"""Re-fetch the exact pinned source responses into a scratch directory, verifying hashes."""

import argparse
import hashlib
import json
import time
import urllib.parse
import urllib.request
from pathlib import Path


def fetch(sources, cache):
    destination = Path(cache).resolve()
    repository = Path(__file__).resolve().parents[3]
    if destination == repository or repository in destination.parents:
        raise ValueError("Downloaded source responses must remain outside the repository.")
    destination.mkdir(parents=True, exist_ok=True)
    for source in json.loads(Path(sources).read_text(encoding="utf-8")):
        target = destination / source["cache"]
        if target.exists() and hashlib.sha256(target.read_bytes()).hexdigest() == source["sha256"]:
            continue
        parameters = {"action": "parse", "oldid": source["revision"],
                      "prop": "text|wikitext|revid", "format": "json"}
        url = "https://aselia.fandom.com/api.php?" + urllib.parse.urlencode(parameters)
        request = urllib.request.Request(url, headers={"User-Agent": "SaveToolkit-reference-research/1.0"})
        with urllib.request.urlopen(request, timeout=60) as response:
            content = response.read()
        if hashlib.sha256(content).hexdigest() != source["sha256"]:
            raise ValueError("Pinned response differs (including template expansion): " + source["title"]
                             + ". Review upstream changes and make a new pack version; do not bypass the hash.")
        target.write_bytes(content)
        print(source["title"], source["revision"])
        time.sleep(0.25)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sources", required=True)
    parser.add_argument("--cache", required=True)
    args = parser.parse_args()
    fetch(args.sources, args.cache)
