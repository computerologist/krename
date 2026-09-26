# KRename

KRename is a preview-first bulk file renamer for Windows, built with C# and WPF.

## Features

- Literal or regular-expression find and replace
- Side-by-side before/after file lists with size, timestamps, and subfolder context
- A lazy-loading drive/folder tree with right-click source and output-folder selection
- Optional separate output folders that preserve recursive subfolder structure
- Green rows for changes, gray rows for harmless no-ops, and red rows for errors that block the operation
- A persistent `View > Columns` chooser for optional metadata columns
- The same column chooser on right-clicking either table's column headers
- An editable source-folder dropdown with remembered recent folders
- Remembered dropdown history for wildcard and rename-option text fields; Enter refreshes from the folder or wildcard field
- Separate remembered source/output path dropdowns above the Before and After panels
- Double-click source filenames for direct inline renaming, or right-click to reveal them in Explorer
- Multiple filename and extension replacement rules
- Per-rule trash controls and one-click clearing of filename or extension rule lists
- Lowercase/uppercase conversion and removal of letters, numbers, spaces, or symbols
- Per-rule case-sensitive matching for filenames, extensions, and split delimiters
- Filename splitting with selectable fields, plus left/right remove-or-keep trimming
- Current or custom date/time components with configurable separators
- Prefixes, suffixes, and padded number or letter sequences
- File masks such as `*.jpg;*.png`
- Recursive subfolder scanning without moving files from their existing directories
- Collision and invalid-name detection before applying changes
- Two-phase renames that safely handle name swaps and chains
- Persistent undo for the most recent successful operation
- Standard File/Edit/View/Run/Options/Help menus and configurable operation logging
- Resizable preview/options panes and dark mode enabled by default
- Automatic source and preview refresh after rename and undo operations

## Build and run

Requires the .NET 9 SDK on Windows.

```powershell
dotnet build KRename.sln
dotnet run --project src/KRename.App/KRename.App.csproj
```

The packaged build is in `publish/`; launch `publish/KRename.exe`.

## Test

```powershell
dotnet run --project tests/KRename.Tests/KRename.Tests.csproj
dotnet run --project tests/KRename.UiTests/KRename.UiTests.csproj
```

The undo journal is stored under `%LOCALAPPDATA%\KRename\last-operation.json`. KRename never overwrites an existing destination file.
