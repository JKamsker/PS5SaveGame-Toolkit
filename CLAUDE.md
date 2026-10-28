See @AGENTS.md for project layout, commands and rules. Plugin authors: @docs/PLUGIN_GUIDE.md.

Division of labour: Claude owns the framework (`src/`, `web/`, `tests/`, docs) and orchestration;
Codex workers (model `gpt-6.1-sol`, high reasoning) implement game plugins in git worktrees under
`.worktrees/<name>` on branches `plugin/<name>`, which Claude reviews and merges.
