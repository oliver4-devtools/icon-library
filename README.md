# Oliver4 Icon Library

An XrmToolBox tool that applies a Fluent UI icon to a Dataverse table in a few clicks: pick a solution, a table and an icon, then one confirmation panel creates or updates the SVG web resource, points the table at it and (optionally) publishes only those two components.

## Install

1. Download the latest `Oliver4.IconLibrary-<version>.zip` from the [Releases](../../releases) page.
2. Close XrmToolBox and copy `Oliver4.IconLibrary.dll` into your XrmToolBox Plugins folder (default `%APPDATA%\MscrmTools\XrmToolBox\Plugins`).
3. If Windows blocked the download, right-click the DLL > Properties > tick **Unblock**.
4. Start XrmToolBox and open **Oliver4 Icon Library**.

Requirements: XrmToolBox 1.2025.10 or later and the Microsoft Edge WebView2 Runtime. Full steps are in `package/INSTALL.txt`.

## What it does and does not do

- Works on custom tables in unmanaged solutions. System tables are hidden because their icons cannot be changed.
- Save and Publish publishes only the web resource and the table it changed. Publishing a table also publishes any other unpublished changes on that table (for example form or view edits).
- If the Default Solution is chosen, the web resource is added only to the Default Solution.
- One icon to one table at a time. Sitemap subarea icons and legacy PNG/ICO icons are not set.

## How it is put together

| Part | Where | Notes |
|---|---|---|
| Icon source | `icons/assets/<Icon Name>/` | One Regular SVG per icon (16 px, or 20 px where no 16 px exists) plus a trimmed `metadata.json`. Copied from microsoft/fluentui-system-icons by `build/extract_icons.py`. `icons/SOURCE.json` records the commit. |
| Category mapping | `build/categories.json` | Curated keyword-to-category rules (20 categories plus Other). Edit and rerun the catalogue build. |
| Catalogue build | `build/build_catalogue.py` | Normalises every SVG (currentColor, 16x16, no styles/scripts/clip wrappers, under 10 KB), assigns categories and writes `src/.../Web/catalogue.json.gz` (embedded in the DLL) plus `build/out/catalogue.json` and `build/out/category-report.txt` for review. Fails the build if any SVG breaks a rule. |
| UI | `src/Oliver4.IconLibrary/Web/` | Plain HTML/CSS/JS, no framework. The UI uses the Windows system fonts Segoe UI and Consolas. `mock.js` is a browser-only stand-in for the host so the UI can be run and screenshotted without Dataverse; it is excluded from the DLL. |
| Host | `src/Oliver4.IconLibrary/*.cs` | XrmToolBox plugin (`Plugin.cs`), WinForms control hosting WebView2 (`IconLibraryControl.cs`), JSON bridge (`HostBridge.cs`), all Dataverse calls (`DataverseService.cs`), runtime SVG check (`SvgValidator.cs`), asset extraction (`WebAssets.cs`), binding to the WebView2 SDK that XrmToolBox ships (`WebView2Assemblies.cs`). |
| Packaging | `package/` | nuspec for the XrmToolBox Tool Library and `Package-Tool.ps1`, which also builds a zip for manual install. |

The tool deploys as a single DLL. It uses the WebView2 SDK that XrmToolBox ships (`Microsoft.Web.WebView2.Core` / `.WinForms` and `WebView2Loader.dll`). It compiles against the minimum version XrmToolBoxPackage requires, and `WebView2Assemblies.cs` (an `AssemblyResolve` handler registered in `Plugin`'s static constructor) binds to whichever version XrmToolBox has. The WebView2 Runtime must be installed on the machine. The embedded web files are extracted to `...\web\<version>` and served to WebView2 through a virtual host name; the browser cache lives in `...\WebView2`.

### Bridge contract (UI to host)

Request `{ id, method, params }`, response `{ id, ok, result | error }`, events `{ event: "progress", steps }` and `{ event: "context", context, reload }`.

Methods: `getContext`, `getAbout`, `openUrl`, `getSolutions`, `getTables`, `getPublishers`, `getTablesUsingWebResource`, `checkWebResourceName`, `findExistingIcon`, `apply`. Shapes are the classes in `Models.cs` (camelCase on the wire). `mock.js` implements the same contract with sample data.

### Dataverse operations used

- Solutions: `solution` where `ismanaged = false` and `isvisible = true`, joined to `publisher` for name and prefix. Default Solution is listed last.
- Tables: `RetrieveAllEntitiesRequest` (EntityFilters.Entity, as-if-published) filtered to the solution's `solutioncomponent` rows (componenttype 1). Shown only when `IsCustomEntity = true`, not intersect/logical, and `IsCustomizable` is not false. Icon state comes from `IconVectorName` matched against the environment's SVG web resources (`webresourcetype = 11`).
- Publishers: `publisher` where `isreadonly = false`.
- Apply, in order: create web resource (`CreateRequest` with `SolutionUniqueName`, so it lands in the solution atomically) or update content or verify a reused one; ensure solution membership (`AddSolutionComponentRequest`, component type 61) ; `RetrieveEntityRequest` + `UpdateEntityRequest` setting `IconVectorName` in the solution's context; `PublishXmlRequest` for just that table and web resource. No rollback; each step is reported.
- Reuse detection compares normalised SVG text of existing SVG web resources with the catalogue.

## Building

Prerequisites: Windows, Visual Studio 2022 (or the .NET SDK with the .NET Framework 4.8 targeting pack), Python 3 (catalogue build only), nuget.exe (Tool Library package only).

On macOS or Linux, `./package/package.sh` builds the DLL with the .NET SDK (8 or later) and writes the same install zip to `package/out`.

```
python build\build_catalogue.py          # regenerates Web\catalogue.json.gz from icons\assets
dotnet build src\Oliver4.IconLibrary -c Release
powershell package\Package-Tool.ps1      # zip + nupkg in package\out
```

To refresh the icons from a newer Fluent release: clone microsoft/fluentui-system-icons, run `python build\extract_icons.py <clone path>`, review `git diff icons/`, rerun the catalogue build and check `build/out/category-report.txt` (anything new landing in Other needs a term in `categories.json`).

To work on the UI without XrmToolBox: `cd src\Oliver4.IconLibrary\Web`, copy `..\..\..\build\out\catalogue.json` next to `index.html`, serve the folder (`python -m http.server 8765`) and open `http://localhost:8765/index.html`. `?screen=tables|browse|confirm|progress|result|fail|about` jumps to a screen.

## Licence

MIT (see `LICENSE`). Bundled third-party material and its licences: `THIRD-PARTY-NOTICES.txt` (also shown in the tool's About screen).
