# Tests

Three sets of automated tests. None of them need XrmToolBox, Windows or a Dataverse environment.

| Set | Folder | What it covers | Needs |
|---|---|---|---|
| Catalogue build | `tests/build` | `build_catalogue.py` SVG normalisation and categorising, and the shipped catalogue (every icon 16x16, currentColor, no scripts/styles, under 10 KB, counts consistent) | Python 3 |
| Host | `tests/HostTests` | `SvgValidator`, `IconCatalogue`, `DataverseService` (reads, create / update / reuse, solution membership, targeted publish, failure reporting, caching) and `HostBridge`, against an in-memory fake Dataverse | .NET SDK 8+, tool built once |
| UI | `tests/ui` | The HTML UI in Chromium (the engine WebView2 uses) against a scripted test host: navigation, search and filters, confirmation panel rules, apply / result / failure screens, About, plus regression tests for previously fixed defects | Node.js 18+ |

## Run everything (macOS or Linux)

1. Build the tool once so its DLLs exist: `./package/package.sh`
2. From the project folder:
   ```
   ./tests/run-tests.sh
   ```
   The first run installs the UI test tools (Playwright and a Chromium browser, about 150 MB) into `tests/ui/node_modules` and the Playwright cache. Later runs skip that.
3. The last line says `ALL TESTS PASSED` or `SOME TESTS FAILED`.

To skip the browser tests: `./tests/run-tests.sh --no-ui`

## Run one set

```
python3 -m unittest discover -s tests/build -v        # catalogue build
cd tests/HostTests && dotnet run -c Release           # host (add a word to filter, e.g. dotnet run -c Release -- Apply)
cd tests/ui && npx playwright test                    # UI (add -g "UI-12" for one test)
```

## Things to know

- Test ids (SV-, IC-, DS-, AP-, HB-, BC-, UI-) appear in the test output.
- The host tests use no NuGet packages: a small built-in runner (`TestFramework.cs`) lets them build and run offline. They compile the tool's non-UI `.cs` files for .NET 8 and use the Dataverse SDK DLLs from the tool's `bin/Release/net48` folder. Newtonsoft.Json comes from the .NET SDK's own folder (same 13.x version) because the .NET Framework copy does not load on .NET 8.
- They test the tool's logic, not the .NET Framework 4.8 binary inside XrmToolBox. The WinForms/WebView2 shell (`IconLibraryControl`, `Plugin`, `WebView2Assemblies`) and real Dataverse behaviour are not covered.
- `FakeOrganizationService` only implements what the tool calls. If the tool starts using another message or query shape, the fake throws `NotSupportedException` so the gap is obvious.
