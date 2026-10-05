/* Scripted stand-in for the C# host, injected before app.js runs (app.js then skips mock.js).
 * Behaviour is driven by window.__TEST_CONFIG (set by the spec) and every call is recorded in window.__calls.
 * Unlike mock.js this host is deterministic and lets a test hold a response until it says so. */
(function () {
  const cfg = window.__TEST_CONFIG || {};
  const calls = (window.__calls = []);
  const gates = {};
  window.__release = name => { (gates[name] || (gates[name] = deferred())).resolve(); };
  function deferred() { let resolve; const p = new Promise(r => (resolve = r)); return { p, resolve }; }
  const gate = name => (gates[name] || (gates[name] = deferred())).p;

  const svgFor = id => (window.__catalogue && (window.__catalogue.icons.find(i => i.id === id) || {}).svg) || "";

  function envData(env) {
    const pfx = env === "env2" ? "fab" : "contoso";
    const pubName = env === "env2" ? "Fabrikam" : "Contoso";
    const wrList = { id: "w1", name: pfx + "_/icons/text_bullet_list_regular.svg", isManaged: false, get svg() { return cfg.maliciousSvg || svgFor("text_bullet_list"); } };
    const wrManaged = { id: "w3", name: "msdyn_/icons/person_regular.svg", isManaged: true, get svg() { return svgFor("person"); } };
    return {
      context: { version: "9.9.9", connected: cfg.connected !== false, orgUrl: env + ".crm11.dynamics.com", orgName: env, userName: "tester" },
      solutions: [
        { id: env + "-s1", uniqueName: pfx + "_core", name: "Core " + env, version: "1.0.0.0", publisherId: env + "-p1", publisherName: pubName, prefix: pfx, isDefault: false },
        { id: env + "-s2", uniqueName: pfx + "_extra", name: "Extra " + env, version: "1.0.0.0", publisherId: env + "-p1", publisherName: pubName, prefix: pfx, isDefault: false },
        { id: env + "-s0", uniqueName: "Default", name: "Default Solution", version: "1.0", publisherId: env + "-p0", publisherName: "Default Publisher", prefix: "cr7a2", isDefault: true }
      ],
      publishers: [
        { id: env + "-p1", name: pubName, uniqueName: pubName, prefix: pfx },
        { id: env + "-p2", name: "Other " + env, uniqueName: "Other", prefix: "oth" },
        { id: env + "-p0", name: "Default Publisher", uniqueName: "DefaultPublisher", prefix: "cr7a2" }
      ],
      tablesBySolution: {
        [env + "-s1"]: [
          { logicalName: pfx + "_batch", displayName: "Batch", iconVectorName: null, iconState: "none", webResource: null },
          { logicalName: pfx + "_context", displayName: "Context", iconVectorName: wrList.name, iconState: "custom", webResource: wrList },
          { logicalName: pfx + "_result", displayName: "Result", iconVectorName: pfx + "_/icons/gone.svg", iconState: "broken", webResource: null },
          { logicalName: pfx + "_partner", displayName: "Partner", iconVectorName: wrManaged.name, iconState: "custom", webResource: wrManaged }
        ],
        [env + "-s2"]: [
          { logicalName: pfx + "_extraone", displayName: "Extra One", iconVectorName: null, iconState: "none", webResource: null }
        ],
        [env + "-s0"]: [
          { logicalName: pfx + "_batch", displayName: "Batch", iconVectorName: null, iconState: "none", webResource: null }
        ]
      },
      wrList
    };
  }

  let env = cfg.env || "env1";
  let data = envData(env);
  window.__testHost = { setEnv(e) { env = e; data = envData(e); }, get env() { return env; } };

  const tick = v => new Promise(r => setTimeout(() => r(v), 5));
  const clone = v => JSON.parse(JSON.stringify(v));

  const api = {
    getContext: () => tick(data.context),
    getSolutions: () => tick(data.solutions),
    warmCaches: () => tick(true),
    warmSvgContent: (() => { let left = 2; return () => tick(left = Math.max(0, left - 1)); })(),
    getTables: async ({ solutionId }) => {
      if (cfg.holdTablesFor === solutionId) await gate("tables:" + solutionId);
      return { tables: data.tablesBySolution[solutionId] || [], hiddenSystemCount: 3 };
    },
    getPublishers: () => tick(data.publishers),
    getTablesUsingWebResource: ({ name }) => tick(Object.values(data.tablesBySolution).flat()
      .filter(t => t.webResource && t.webResource.name === name).map(t => ({ logicalName: t.logicalName, displayName: t.displayName }))
      .concat(name === data.wrList.name ? [{ logicalName: "other_shared", displayName: "Other Shared" }] : [])),
    checkWebResourceName: ({ name }) => tick({ exists: /taken/.test(name), isManaged: false }),
    findExistingIcon: ({ iconId }) => tick(iconId === "text_bullet_list" ? [{ id: "w1", name: data.wrList.name, isManaged: false, inSolution: true }] : []),
    apply: async req => {
      const steps = [];
      const label = { create: "Create web resource", update: "Update web resource", reuse: "Verify existing web resource" }[req.action];
      steps.push({ key: "webresource", label, status: "pending", detail: req.webResourceName });
      if (req.action !== "update") steps.push({ key: "solution", label: "Add to solution", status: "pending", detail: req.solutionUniqueName });
      steps.push({ key: "table", label: "Update table icon", status: "pending", detail: req.tableLogicalName });
      steps.push({ key: "publish", label: req.publish ? "Publish web resource and table" : "Publish", status: req.publish ? "pending" : "skipped", detail: req.publish ? "" : "not requested" });
      for (const s of steps) {
        if (s.status === "skipped") continue;
        s.status = "running"; window.__host.emit("progress", { steps: clone(steps) });
        if (cfg.holdApply) await gate("apply");
        if (cfg.failStep === s.key) {
          s.status = "failed"; s.message = "insufficient privileges"; s.error = "missing prvWriteEntity";
          return { success: false, tableUpdated: false, steps, error: s.label + " failed" };
        }
        s.status = "done";
      }
      const t = Object.values(data.tablesBySolution).flat().find(x => x.logicalName === req.tableLogicalName);
      if (t) { t.iconState = "custom"; t.iconVectorName = req.webResourceName; t.webResource = { id: "new", name: req.webResourceName, isManaged: false, svg: svgFor(req.iconId) }; }
      return { success: true, tableUpdated: true, steps };
    },
    getAbout: () => tick({ version: "9.9.9", notices: "MIT License\nCopyright (c) 2020 Microsoft Corporation\nfluentui-system-icons", homepage: "https://example.test", repository: "https://example.test/repo" }),
    openUrl: () => tick(true)
  };

  window.__mockHost = {
    call(method, params) {
      calls.push({ method, params: clone(params || {}) });
      return api[method] ? Promise.resolve(api[method](params || {})).then(clone) : Promise.reject(new Error("test host: unknown method " + method));
    }
  };

  // keep a copy of the catalogue the page loads so the host can hand back real SVGs
  const _fetch = window.fetch;
  window.fetch = async function (url, opts) {
    const res = await _fetch(url, opts);
    if (String(url).includes("catalogue.json")) res.clone().json().then(j => { window.__catalogue = j; });
    return res;
  };
})();
