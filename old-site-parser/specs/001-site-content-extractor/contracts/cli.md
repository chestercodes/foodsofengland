# CLI Contract: OldSiteParser

## Command Syntax

```
OldSiteParser --source-folder <path> --database <path> --working-dir <path> [--step <n>]...
```

## Arguments

| Name | Short | Type | Required | Default | Description |
|------|-------|------|----------|---------|-------------|
| `--source-folder` | `-s` | directory path | Yes | — | Root folder of scraped website files |
| `--database` | `-d` | file path | Yes | — | SQLite database file path (created if missing) |
| `--working-dir` | `-w` | directory path | Yes | — | Output directory for rendered files |
| `--step` | `-n` | int (1–6) | No | all | Step(s) to run. Repeatable. |

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | All requested steps completed successfully |
| 1 | One or more steps completed with warnings (logged errors for individual files, but pipeline continued) |
| 2 | Fatal error (e.g. source folder not found, database cannot be opened) |

## Console Output

- **Step 1**: Prints duplicate file report (groups of files with identical hashes). Prints orphan warning for images not referenced by any HTML file (Step 3).
- **Steps 1–6**: Prints step name, file count, and elapsed time. Errors are printed to stderr with file path and description.
- **All steps**: Summary line at completion: `"Completed steps [1,2,3,...] in X.Xs. N errors logged."`

## Step Dependencies

Steps must be run in order. Running Step N requires that Steps 1 through N-1 have already populated the database. The app validates this before each step and exits with a clear error if prerequisites are missing.
