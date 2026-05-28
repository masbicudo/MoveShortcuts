# TODO

This file captures future ideas that are not ready to implement yet.

## Future Ideas

### Config Management Commands

Possible command family:

```text
mvshct list
mvshct search <text>
mvshct add <program>
mvshct remove <name>
mvshct edit
mvshct doctor
mvshct cache refresh
mvshct path add --user
mvshct path add --machine
```

`init` should remain the first-time broad setup flow. These commands would be
for day-to-day maintenance.

Important caution: the current config file may contain comments and careful
ordering. A future `add` or `remove` command must not casually deserialize and
rewrite the whole JSON file if that destroys comments, grouping, or formatting.
Use a comment-preserving JSON editing strategy, or add commands only after the
config format/editor story is solid.

### Program Discovery Cache

For commands such as `mvshct add <program>` and completion, keep a small
candidate cache containing names from:

- Desktop shortcuts
- Start Menu shortcuts
- UWP / AppsFolder cache
- existing config keys

The cache should have a TTL, perhaps around one hour. Desktop scans may be cheap
enough to refresh live. Start Menu scans can use a breadth-first traversal with a
short time limit and maximum depth 2, starting from the `Programs` folder. In
that model, depth 1 means direct children of `Programs` itself.

UWP should use the existing cache unless explicitly refreshed.

### Search Before Add

Avoid dumping hundreds of candidates into the terminal. Prefer:

```text
mvshct search visual
mvshct add "Visual Studio Code"
```

`add` can accept exact names first, then later high-confidence fuzzy matches.
Ambiguous matches should ask the user or print choices instead of guessing.

### PowerShell Completion

PowerShell argument completion is synchronous and runs when Tab is pressed. It
should stay fast and avoid expensive full scans.

Completion policy idea:

- complete command names and options freely
- complete `add <prefix>` only when the prefix has at least 2 or 3 characters
- `mvshct add vi<TAB>` should show candidates whose names start with `vi*`
  first, such as `Visual Studio Code`
- return a bounded number of candidates
- use cached candidates for UWP
- avoid full Start Menu or AppsFolder enumeration inside the completer

Inline "ghost text" suggestions would require a PSReadLine predictor, which is
a larger shell integration project. Defer that unless MoveShortcuts becomes a
daily-driver shell tool.

### Icon Handling

`getFavIcon` is currently unreliable and can waste time. Keep it disabled by
default and consider deprecating automatic favicon fetching during normal runs.

A better future shape may be explicit icon support:

```json
"Some Site": {
  "Target": "https://example.com",
  "Icon": "C:\\Shortcuts\\Icons\\example.ico"
}
```

or a separate command:

```text
mvshct fetch-icons
```

Normal shortcut processing should remain local and fast.
