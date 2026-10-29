"""Check the live export against the existing canonical OpenAPI contract, without re-pinning."""

import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CONTRACT = ROOT / "tests/SaveToolkit.Api.Performance.Tests/OpenApi.v1.json"
METHODS = {"get", "put", "post", "delete", "options", "head", "patch", "trace"}


def normalize(value):
    # Same normalization contract as OpenApiSnapshot.cs: normalize strings, preserve arrays,
    # compare object keys independent of their input order and exclude host-dependent servers.
    if isinstance(value, dict):
        return {key: normalize(item) for key, item in value.items()}
    if isinstance(value, list):
        return [normalize(item) for item in value]
    return value.replace("\r\n", "\n").replace("\r", "\n") if isinstance(value, str) else value


def check(schema):
    actual = json.loads(schema.read_text(encoding="utf-8-sig"))
    expected = json.loads(CONTRACT.read_text(encoding="utf-8"))
    actual.pop("servers", None)
    if normalize(actual) != normalize(expected):
        raise ValueError("Generated OpenAPI differs from the reviewed contract. Review metadata and "
                         "follow docs/API.md#openapi-contract-and-regression-guard; this build never re-pins it.")
    operations = sum(method in METHODS for path in actual["paths"].values() for method in path)
    if not operations or not actual.get("components", {}).get("schemas"):
        raise ValueError("OpenAPI must contain operations and named schemas.")
    # Keep the metadata intact and replace only the variable origin with the local-use default.
    actual["servers"] = [{"url": "http://127.0.0.1:5180", "description": "Your locally running toolkit"}]
    schema.write_text(json.dumps(actual, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"OpenAPI contract: {operations} operations; {len(actual['components']['schemas'])} schemas match.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--schema", required=True, type=Path)
    check(parser.parse_args().schema)
