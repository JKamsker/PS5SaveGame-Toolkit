"""Resolve an exact size-check base, recovering rewritten topic-branch pushes."""
import os
import subprocess


def git(*arguments, required=True):
    result = subprocess.run(["git", *arguments], capture_output=True, text=True)
    if required and result.returncode:
        raise RuntimeError(f"git {' '.join(arguments)} failed: {result.stderr.strip()}")
    return result


def exists(base):
    return git("cat-file", "-e", f"{base}^{{commit}}", required=False).returncode == 0


def main():
    event = os.environ.get("EVENT_NAME", "")
    base = (os.environ.get("PR_BASE_SHA", "") if event == "pull_request"
            else os.environ.get("EVENT_BEFORE", ""))
    if base == "0" * 40:
        base = ""
    if event == "pull_request" and not base:
        raise RuntimeError("Pull request base SHA is required.")
    if base and not exists(base):
        # Full history only includes reachable commits. A force-push can remove before.
        git("fetch", "--no-tags", "origin", base, required=False)
        if not exists(base):
            ref = os.environ.get("GITHUB_REF", "")
            if event != "push" or not ref.startswith("refs/heads/") or ref == "refs/heads/main":
                raise RuntimeError(f"Unable to resolve exact C# size guard base '{base}'.")
            print("::warning::Previous push commit is unavailable on a rewritten branch; checking against origin/main.")
            base = ""
    if not base:
        git("fetch", "--no-tags", "origin", "+main:refs/remotes/origin/main")
        base = git("rev-parse", "refs/remotes/origin/main").stdout.strip()
    if not exists(base):
        raise RuntimeError(f"Unable to resolve C# size guard base '{base}'.")
    with open(os.environ["GITHUB_ENV"], "a", encoding="utf-8") as output:
        output.write(f"CSHARP_SIZE_BASE={base}\n")
    print(f"Using C# size guard base {base}")


if __name__ == "__main__":
    main()
