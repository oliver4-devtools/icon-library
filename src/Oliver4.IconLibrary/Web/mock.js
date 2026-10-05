/* Mock host for developing the UI in a browser without XrmToolBox or Dataverse.
 * Loaded automatically by app.js when no WebView2 bridge is present. Not shipped in the plugin package.
 * Query string flags: ?screen=solutions|tables|browse|confirm|progress|result|fail|about  (jumps to a screen after load)
 */
(function () {
  const delay = (ms, v) => new Promise(r => setTimeout(() => r(v), ms));
  const svgFor = id => {
    // filled in once the catalogue is available via window.__mockCatalogue (set by the fetch shim below)
    const c = window.__mockCatalogue; if (!c) return "";
    const ic = c.icons.find(i => i.id === id); return ic ? ic.svg : "";
  };

  const solutions = [
    { id: "s1", uniqueName: "contoso_documentprocessing", name: "Document Processing", version: "1.2.0.4", publisherId: "p1", publisherName: "Contoso", prefix: "contoso", isDefault: false },
    { id: "s2", uniqueName: "contoso_integrationcore", name: "Integration Core", version: "1.0.0.12", publisherId: "p1", publisherName: "Contoso", prefix: "contoso", isDefault: false },
    { id: "s3", uniqueName: "contoso_serviceextensions", name: "Customer Service Extensions", version: "2.4.1.0", publisherId: "p1", publisherName: "Contoso", prefix: "contoso", isDefault: false },
    { id: "s4", uniqueName: "fab_fieldoperations", name: "Field Operations", version: "0.9.0.3", publisherId: "p2", publisherName: "Fabrikam", prefix: "fab", isDefault: false },
    { id: "s5", uniqueName: "contoso_reportingpack", name: "Reporting Pack", version: "1.0.0.1", publisherId: "p1", publisherName: "Contoso", prefix: "contoso", isDefault: false },
    { id: "s6", uniqueName: "Default", name: "Default Solution", version: "1.0", publisherId: "p0", publisherName: "Default Publisher for contoso-dev", prefix: "cr7a2", isDefault: true }
  ];
  const publishers = [
    { id: "p1", name: "Contoso", uniqueName: "Contoso", prefix: "contoso" },
    { id: "p2", name: "Fabrikam", uniqueName: "Fabrikam", prefix: "fab" },
    { id: "p0", name: "Default Publisher for contoso-dev", uniqueName: "DefaultPublishercontosodev", prefix: "cr7a2" }
  ];
  const wr = {
    list: { id: "w1", name: "contoso_/icons/text_bullet_list_regular.svg", isManaged: false, get svg() { return svgFor("text_bullet_list"); } },
    rule: { id: "w2", name: "contoso_/icons/gavel_regular.svg", isManaged: false, get svg() { return svgFor("gavel"); } },
    managed: { id: "w3", name: "msdyn_/icons/person_regular.svg", isManaged: true, get svg() { return svgFor("person"); } }
  };
  const tables = [
    { logicalName: "contoso_documentbatch", displayName: "Document Batch", iconVectorName: null, iconState: "none", webResource: null },
    { logicalName: "contoso_documentprocessingcontext", displayName: "Document Processing Context", iconVectorName: wr.list.name, iconState: "custom", webResource: wr.list },
    { logicalName: "contoso_processingrule", displayName: "Processing Rule", iconVectorName: wr.rule.name, iconState: "custom", webResource: wr.rule },
    { logicalName: "contoso_extractionresult", displayName: "Extraction Result", iconVectorName: "contoso_/icons/old.svg", iconState: "broken", webResource: null },
    { logicalName: "contoso_reviewtask", displayName: "Review Task", iconVectorName: null, iconState: "none", webResource: null },
    { logicalName: "contoso_vendormapping", displayName: "Vendor Mapping", iconVectorName: null, iconState: "none", webResource: null },
    { logicalName: "contoso_auditentry", displayName: "Audit Entry", iconVectorName: wr.list.name, iconState: "custom", webResource: wr.list },
    { logicalName: "contoso_partnerperson", displayName: "Partner Person", iconVectorName: wr.managed.name, iconState: "custom", webResource: wr.managed }
  ];

  const params = new URLSearchParams(location.search);
  const failMode = params.get("screen") === "fail";

  const api = {
    getContext: () => delay(80, { version: "1.2.3", connected: true, orgUrl: "contoso-dev.crm11.dynamics.com", orgName: "Contoso Dev", userName: "maker@contoso.com" }),
    getSolutions: () => delay(350, solutions),
    warmCaches: () => delay(400, true),
    warmSvgContent: (() => { let left = 3; return () => delay(300, left = Math.max(0, left - 1)); })(),
    getTables: ({ solutionUniqueName }) => delay(400, { tables: solutionUniqueName === "Default" ? tables : tables.slice(0, 6), hiddenSystemCount: 312 }),
    getPublishers: () => delay(200, publishers),
    getTablesUsingWebResource: ({ name }) => delay(150, tables.filter(t => t.webResource && t.webResource.name === name).map(t => ({ logicalName: t.logicalName, displayName: t.displayName }))),
    checkWebResourceName: ({ name }) => delay(300, { exists: /taken|text_bullet_list|gavel/.test(name), isManaged: false }),
    findExistingIcon: ({ iconId }) => delay(200, iconId === "text_bullet_list" ? [{ id: "w1", name: wr.list.name, isManaged: false, inSolution: true }] : iconId === "person" ? [{ id: "w3", name: wr.managed.name, isManaged: true, inSolution: false }] : []),
    apply: async (req) => {
      const steps = [];
      if (req.action === "create") steps.push({ key: "webresource", label: "Create web resource", status: "pending", detail: req.webResourceName });
      if (req.action === "update") steps.push({ key: "webresource", label: "Update web resource", status: "pending", detail: req.webResourceName });
      if (req.action === "reuse") steps.push({ key: "webresource", label: "Verify existing web resource", status: "pending", detail: req.webResourceName });
      if (req.action !== "update") steps.push({ key: "solution", label: "Add to solution", status: "pending", detail: req.solutionUniqueName });
      steps.push({ key: "table", label: "Update table icon", status: "pending", detail: req.tableLogicalName });
      steps.push({ key: "publish", label: req.publish ? "Publish web resource and table" : "Publish", status: req.publish ? "pending" : "skipped", detail: req.publish ? "" : "not requested" });
      const emit = () => window.__host.emit("progress", { steps: steps.map(s => ({ ...s })) });
      for (const s of steps) {
        if (s.status === "skipped") continue;
        s.status = "running"; emit(); await delay(700);
        if (failMode && s.key === "table") {
          s.status = "failed"; s.message = "insufficient privileges"; s.error = "Principal user (Id=…) is missing prvWriteEntity privilege. Microsoft.Crm.CrmSecurityException";
          emit(); return { success: false, steps, tableUpdated: false };
        }
        s.status = "done"; emit();
      }
      const t = tables.find(x => x.logicalName === req.tableLogicalName);
      if (t) {
        const cat = window.__mockCatalogue; const ic = cat && cat.icons.find(i => i.id === req.iconId);
        t.iconState = "custom"; t.iconVectorName = req.webResourceName;
        t.webResource = { id: req.webResourceId || "w" + Date.now(), name: req.webResourceName, isManaged: false, svg: ic ? ic.svg : "" };
      }
      return { success: true, steps, tableUpdated: true };
    },
    getAbout: async () => {
      const r = await fetch("THIRD-PARTY-NOTICES.txt").catch(() => null);
      const notices = r && r.ok ? await r.text() : "MIT License\nCopyright (c) 2020 Microsoft Corporation\n\nCopy THIRD-PARTY-NOTICES.txt next to index.html to see the full notices here.";
      return { notices, homepage: "https://www.oliver4-devtools.com", repository: "https://github.com/oliver4-devtools/icon-library" };
    },
    openUrl: ({ url }) => { window.open(url, "_blank"); return Promise.resolve(true); }
  };

  // Results are copied, as they would be coming over the real bridge, so the UI can change what it receives.
  const copy = v => v === undefined ? v : JSON.parse(JSON.stringify(v));
  window.__mockHost = { call: (m, p) => api[m] ? Promise.resolve(api[m](p || {})).then(copy) : Promise.reject(new Error("mock: unknown method " + m)) };

  // capture the catalogue when app.js fetches it so the mock can hand back real SVGs
  const _fetch = window.fetch;
  window.fetch = async function (url, opts) {
    const res = await _fetch(url, opts);
    if (String(url).includes("catalogue.json")) { const clone = res.clone(); clone.json().then(j => { window.__mockCatalogue = j; }); }
    return res;
  };

  // optional: jump to a screen for screenshots
  const target = params.get("screen");
  if (target) {
    const wait = (fn, tries = 60) => new Promise((res, rej) => { const t = setInterval(() => { if (fn()) { clearInterval(t); res(); } else if (--tries <= 0) { clearInterval(t); rej(new Error("timeout")); } }, 100); });
    (async () => {
      await wait(() => document.querySelector(".list-row"));
      if (target === "solutions") return;
      const next = () => document.querySelector(".list-actions .btn-primary").click();
      document.querySelectorAll(".list-row")[0].click(); next();
      await wait(() => document.querySelector(".list-row .state-icon"));
      if (target === "tables") return;
      document.querySelectorAll(".list-row")[0].click(); next();
      await wait(() => document.querySelector(".tile"));
      const search = document.querySelector(".cats-search input");
      search.value = "stack"; search.dispatchEvent(new Event("input"));
      await wait(() => [...document.querySelectorAll(".tile .n")].some(n => n.textContent === "Stack"));
      [...document.querySelectorAll(".tile")].find(n => n.querySelector(".n").textContent === "Stack").click();
      if (target === "browse") return;
      if (target === "about") { document.getElementById("btnAbout").click(); return; }
      document.querySelector(".rail-cta .btn-primary").click();
      await wait(() => document.querySelector("#btnSavePublish") && !document.querySelector("#btnSavePublish").disabled);
      if (target === "confirm") return;
      document.querySelector("#btnSavePublish").click();
      if (target === "progress") { await wait(() => document.querySelectorAll(".prow.done").length >= 1); return; }
      await wait(() => document.querySelector(".result-hero"), 120);
    })().catch(e => console.warn("mock navigation:", e.message));
  }
})();
