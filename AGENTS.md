# AGENTS.md

These guidelines apply to both AI agents and human contributors working on
MoveShortcuts.

The terms MUST, MUST NOT, SHOULD, SHOULD NOT, and MAY are to be interpreted as
described in RFC 2119.

## Principles

- Preserve user shortcuts and PATH behavior.
- Prefer explicit, inspectable configuration.
- Fail clearly on project bugs and invalid local state.
- Degrade safely on Windows shell, filesystem, registry, and external-command
  failures.
- Keep changes small, tested, and committed at useful checkpoints.
- Avoid "works on my machine" behavior; document local assumptions.

## Project Context

- This is a Windows-focused C#/.NET tool.
- The active config is `move-shortcuts-options.json` in the current working
  directory.
- The normal debug working directory is
  `MoveShortcuts/bin/Debug/net10.0-windows`.
- `C:\Shortcuts` is commonly on PATH, so root-level outputs are command-surface
  changes, not just files.
- PATH setup MUST NOT reorder an existing shortcuts entry unless the user asks
  for an explicit placement mode.
- Build output, local configs, logs, and generated caches MUST NOT be committed
  unless intentionally added as test fixtures or documentation examples.

## Safety Policy

- Root shortcuts MUST NOT shadow external vendor/system commands.
- Group folders are organizational mirrors and SHOULD NOT be treated as PATH
  surfaces.
- Prefer vendor `.lnk` and `.url` files when they exist; they often preserve
  arguments, icons, working directories, and AppUserModelIDs.
- Cleanup behavior, especially deleting Desktop shortcuts, MUST be explicit in
  config or prompted during `init`.
- Partial failures SHOULD skip the affected item, report why, and continue when
  the remaining work is safe.
- Exceptions MUST NOT be swallowed silently. If an exception is intentionally
  non-fatal, the reason should be obvious from context or logged.

## Configuration And Cache Policy

- Application behavior SHOULD come from config, command-line options, or stable
  Windows APIs, not hidden IDE state.
- Defaults MAY exist, but they MUST be conservative and documented when user
  visible.
- Cache files MAY improve performance, but they MUST have clear invalidation
  rules or conservative reuse checks.
- UWP cache behavior MUST treat AppsFolder as the source of truth.
- External target caches MAY reuse a result only while the resolved file or
  folder still exists.
- Cache schema changes SHOULD include tests for old, missing, invalid, and stale
  cache files.

## Testing Policy

- Run `dotnet test --no-restore` before committing code changes when practical.
- Add focused tests for behavior changes, bug fixes, cache invalidation, PATH
  safety, and edge cases found during implementation.
- Tests MUST NOT depend on the developer's actual Desktop, Start Menu, PATH, or
  shortcuts folder unless explicitly written as a local smoke test outside the
  committed test suite.
- Use temporary directories for filesystem tests.
- Smoke tests against the real debug output are useful, but do not replace unit
  tests for risky behavior.

## Optimization Policy

- Profile before optimizing unless the bottleneck is already directly measured.
- Optimize one idea at a time and commit each successful optimization step.
- Each optimization SHOULD include:
  - a baseline or profile observation
  - a wall-clock measurement after the change
  - tests when behavior or cache correctness is affected
  - documentation when tradeoffs are user-visible
- Profiling external commands such as `es` or `where` MUST use enough dynamic
  command entries to have a realistic chance of moving wall-clock time.
- A profile item that disappears but does not improve real wall-clock time MUST
  be rolled back in git unless there is another clear maintenance or correctness
  reason to keep it.
- Prefer conservative fast paths that fall back to the older correct path.
- Do not trade shortcut correctness, PATH safety, or user data safety for small
  timing wins.
- Record meaningful performance work in `docs/optimizations.md`.

## Git Policy

- Keep commits small and topic-focused.
- Commit each successful optimization separately.
- Do not mix research, behavior changes, docs, and unrelated cleanup unless they
  are part of the same coherent checkpoint.
- Do not revert user changes unless explicitly asked.
- When an experiment fails to improve wall-clock performance, roll it back
  cleanly instead of leaving dead complexity behind.

## Research Policy

- Use `research/` for exploratory scripts, datasets, and notes that are useful
  to future decisions.
- Do not commit local machine datasets containing installed-app or private path
  details unless they have been sanitized.
- Research code SHOULD be commented enough that the next reader can understand
  what was measured and why.
- Promote research into production only after it has a clear correctness story,
  performance value, and tests.
