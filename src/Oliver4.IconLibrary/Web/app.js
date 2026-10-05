/* Oliver4 Icon Library - UI logic.
 *
 * Runs inside a WebView2 control hosted by the XrmToolBox plugin. All Dataverse work is done by the
 * C# host; this file only renders the screens and talks to the host through a small JSON message
 * bridge (see `host` below).
 */
(function () {
  "use strict";

  // ------------------------------------------------------------------ bridge
  const host = (function () {
    const pending = new Map();
    let nextId = 1;
    const listeners = {};
    const webview = window.chrome && window.chrome.webview;

    function call(method, params) {
      if (!webview) {
        if (window.__mockHost) return window.__mockHost.call(method, params || {});
        return Promise.reject(new Error("No host bridge available"));
      }
      return new Promise((resolve, reject) => {
        const id = nextId++;
        pending.set(id, { resolve, reject });
        webview.postMessage(JSON.stringify({ id, method, params: params || {} }));
      });
    }

    function on(event, fn) { (listeners[event] = listeners[event] || []).push(fn); }
    function emit(event, data) { (listeners[event] || []).forEach(fn => fn(data)); }

    if (webview) {
      webview.addEventListener("message", ev => {
        let msg = ev.data;
        if (typeof msg === "string") { try { msg = JSON.parse(msg); } catch (e) { return; } }
        if (!msg) return;
        if (msg.event) { emit(msg.event, msg); return; }
        const p = pending.get(msg.id);
        if (!p) return;
        pending.delete(msg.id);
        if (msg.ok) p.resolve(msg.result); else p.reject(new Error(msg.error || "Unknown host error"));
      });
    }
    return { call, on, emit, isHosted: !!webview };
  })();
  window.__host = host;

  // ------------------------------------------------------------------ helpers
  const $ = (sel, root) => (root || document).querySelector(sel);
  const el = (tag, attrs, ...children) => {
    const node = document.createElement(tag);
    if (attrs) for (const [k, v] of Object.entries(attrs)) {
      if (k === "class") node.className = v;
      else if (k === "html") node.innerHTML = v;
      else if (k.startsWith("on")) node.addEventListener(k.slice(2), v);
      else if (v !== null && v !== undefined && v !== false) node.setAttribute(k, v === true ? "" : v);
    }
    for (const c of children.flat()) {
      if (c === null || c === undefined || c === false) continue;
      node.appendChild(typeof c === "string" ? document.createTextNode(c) : c);
    }
    return node;
  };
  const esc = s => String(s == null ? "" : s).replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
  /* SVG web resources read from the environment are untrusted: anyone with customisation rights can save one.
     Rendering them with innerHTML would run event handlers such as onerror/onmouseover inside the tool, where
     script can reach the host bridge. Keep only drawing elements and drop event, link and script-like attributes. */
  const SAFE_SVG_ELEMENTS = new Set(["svg", "g", "path", "circle", "ellipse", "rect", "line", "polyline", "polygon", "defs", "clippath", "mask", "lineargradient", "radialgradient", "stop", "title", "desc"]);
  function safeSvg(svg) {
    if (!svg) return "";
    let doc;
    try { doc = new DOMParser().parseFromString(svg, "image/svg+xml"); } catch (e) { return ""; }
    const root = doc && doc.documentElement;
    if (!root || root.localName !== "svg" || doc.getElementsByTagName("parsererror").length) return "";
    const clean = node => {
      for (const child of [...node.children]) {
        if (!SAFE_SVG_ELEMENTS.has(child.localName.toLowerCase())) { child.remove(); continue; }
        clean(child);
      }
      for (const a of [...node.attributes]) {
        const n = a.name.toLowerCase(), v = a.value.toLowerCase();
        if (n.startsWith("on") || n === "href" || n.endsWith(":href") || v.includes("javascript:") || v.includes("data:")) node.removeAttribute(a.name);
      }
    };
    clean(root);
    return new XMLSerializer().serializeToString(root);
  }
  /* Tables come from the host with the web resource SVG inline; clean it once on arrival. */
  function sanitiseTables(tables) {
    for (const t of tables || []) if (t.webResource && t.webResource.svg) t.webResource.svg = safeSvg(t.webResource.svg);
    return tables;
  }
  const svgNode = (svg, cls) => { const d = el("div", { class: cls || "", html: svg || "" }); return d; };
  const debounce = (fn, ms) => { let t; return (...a) => { clearTimeout(t); t = setTimeout(() => fn(...a), ms); }; };
  const fmt = n => n.toLocaleString("en-GB");

  const ICONS = {
    search: '<svg viewBox="0 0 16 16" fill="currentColor"><path d="M6.5 2a4.5 4.5 0 0 1 3.6 7.2l3.35 3.35a.5.5 0 0 1-.7.7L9.4 9.9A4.5 4.5 0 1 1 6.5 2Zm0 1a3.5 3.5 0 1 0 0 7 3.5 3.5 0 0 0 0-7Z"/></svg>',
    grid: '<svg viewBox="0 0 16 16" fill="currentColor"><path d="M2 3.5A1.5 1.5 0 0 1 3.5 2h9A1.5 1.5 0 0 1 14 3.5v9a1.5 1.5 0 0 1-1.5 1.5h-9A1.5 1.5 0 0 1 2 12.5v-9ZM3 7h4.5V3h-4a.5.5 0 0 0-.5.5V7Zm0 1v4.5a.5.5 0 0 0 .5.5h4V8H3Zm5.5 5h4a.5.5 0 0 0 .5-.5V8H8.5v5Zm4.5-6V3.5a.5.5 0 0 0-.5-.5h-4v4H13Z"/></svg>',
    edit: '<svg viewBox="0 0 16 16" fill="currentColor"><path d="M11.85 1.85a1.2 1.2 0 0 1 1.7 0l.6.6a1.2 1.2 0 0 1 0 1.7L6.9 11.4a1.5 1.5 0 0 1-.7.4l-2.3.6a.5.5 0 0 1-.6-.6l.6-2.3c.07-.27.2-.5.4-.7l7.55-7.55Zm1 .7a.2.2 0 0 0-.3 0l-.75.75 1.9 1.9.75-.75a.2.2 0 0 0 0-.3l-.6-.6-1-1ZM11.1 4l-6.1 6.1a.5.5 0 0 0-.13.23l-.4 1.5 1.5-.4a.5.5 0 0 0 .23-.13L12.3 5.2 11.1 4Z"/></svg>',
    warn: '<svg viewBox="0 0 16 16" fill="currentColor"><path d="M7.13 2.53a1 1 0 0 1 1.74 0l5.5 9.97A1 1 0 0 1 13.5 14h-11a1 1 0 0 1-.87-1.5l5.5-9.97ZM8 3.01 2.5 13h11L8 3.01ZM8 6a.5.5 0 0 1 .5.5v3a.5.5 0 0 1-1 0v-3A.5.5 0 0 1 8 6Zm0 6.25a.75.75 0 1 1 0-1.5.75.75 0 0 1 0 1.5Z"/></svg>',
    info: '<svg viewBox="0 0 16 16" fill="currentColor"><path d="M8 2a6 6 0 1 1 0 12A6 6 0 0 1 8 2Zm0 1a5 5 0 1 0 0 10A5 5 0 0 0 8 3Zm0 4a.5.5 0 0 1 .5.5v3a.5.5 0 0 1-1 0v-3A.5.5 0 0 1 8 7Zm0-1.75a.75.75 0 1 1 0-1.5.75.75 0 0 1 0 1.5Z"/></svg>',
    check: '<svg viewBox="0 0 16 16" fill="currentColor"><path d="M13.85 3.65a.5.5 0 0 1 0 .7l-7.5 7.5a.5.5 0 0 1-.7 0l-3.5-3.5a.5.5 0 1 1 .7-.7L6 10.79l7.15-7.14a.5.5 0 0 1 .7 0Z"/></svg>',
    x: '<svg viewBox="0 0 16 16" fill="currentColor"><path d="M3.15 3.15a.5.5 0 0 1 .7 0L8 7.29l4.15-4.14a.5.5 0 0 1 .7.7L8.71 8l4.14 4.15a.5.5 0 0 1-.7.7L8 8.71l-4.15 4.14a.5.5 0 0 1-.7-.7L7.29 8 3.15 3.85a.5.5 0 0 1 0-.7Z"/></svg>',
    arrowR: '<svg viewBox="0 0 16 16" fill="currentColor"><path d="M8.65 3.15a.5.5 0 0 0-.7.7L11.29 7.5H3a.5.5 0 0 0 0 1h8.29l-3.34 3.65a.5.5 0 0 0 .7.7l4.2-4.5a.5.5 0 0 0 0-.7l-4.2-4.5Z"/></svg>',
    arrowL: '<svg viewBox="0 0 16 16" fill="currentColor"><path d="M7.35 3.15a.5.5 0 0 1 .7.7L4.71 7.5H13a.5.5 0 0 1 0 1H4.71l3.34 3.65a.5.5 0 0 1-.7.7l-4.2-4.5a.5.5 0 0 1 0-.7l4.2-4.5Z"/></svg>',
    reload: '<svg viewBox="0 0 16 16" fill="currentColor"><path d="M8 2.5A5.5 5.5 0 0 0 2.5 8a.5.5 0 0 1-1 0 6.5 6.5 0 0 1 11.2-4.5V2.5a.5.5 0 0 1 1 0V5a.5.5 0 0 1-.5.5h-2.5a.5.5 0 0 1 0-1h1.4A5.48 5.48 0 0 0 8 2.5Zm5.5 5a.5.5 0 0 1 1 0A6.5 6.5 0 0 1 3.3 12.5v1a.5.5 0 0 1-1 0V11a.5.5 0 0 1 .5-.5h2.5a.5.5 0 0 1 0 1H3.9A5.5 5.5 0 0 0 13.5 7.5Z"/></svg>',
    // Sitemap icons for the standard tables in the preview
    smAccount: '<svg viewBox="0 0 20 20" fill="none"><rect x="4.5" y="2.5" width="11" height="15" rx="1" stroke="currentColor" stroke-width="1.3"/><path d="M8.5 17.5v-3h3v3M7.5 5.75h1.2M11.3 5.75h1.2M7.5 8.75h1.2M11.3 8.75h1.2M7.5 11.75h1.2M11.3 11.75h1.2" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/></svg>',
    smContact: '<svg viewBox="0 0 20 20" fill="none"><circle cx="10" cy="6.5" r="3" stroke="currentColor" stroke-width="1.3"/><path d="M4.5 16.5c0-3 2.5-4.5 5.5-4.5s5.5 1.5 5.5 4.5" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/></svg>',
    smCase: '<svg viewBox="0 0 20 20" fill="none"><path d="M4.5 10.5V8.5a5.5 5.5 0 0 1 11 0v2" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/><rect x="3.5" y="10" width="3" height="4.5" rx="1.2" stroke="currentColor" stroke-width="1.3"/><rect x="13.5" y="10" width="3" height="4.5" rx="1.2" stroke="currentColor" stroke-width="1.3"/><path d="M15 14.8c0 1.7-2 2.2-4 2.2" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/></svg>',
    smDefault: '<svg viewBox="0 0 20 20" fill="none"><rect x="3.5" y="3.5" width="13" height="13" rx="1.5" stroke="currentColor" stroke-width="1.3"/><path d="M3.5 8h13M8 8v8.5" stroke="currentColor" stroke-width="1.3"/></svg>'
  };

  // ------------------------------------------------------------------ state
  const S = {
    context: { version: "", connected: false, orgUrl: "", orgName: "" },
    catalogue: null,           // { source, categories, icons }
    iconById: new Map(),
    screen: "loading",         // loading | disconnected | solutions | tables | browse
    solutions: null, solutionsLoading: false, solutionsError: null, solutionSearch: "",
    solution: null,
    tables: null, tablesLoading: false, tablesError: null, tableSearch: "", hiddenSystemCount: 0,
    table: null,
    iconSearch: "", category: "all", icon: null,
    publishers: null,
    modal: null                // null | { kind: 'confirm'|'progress'|'result'|'about', ... }
  };

  // ------------------------------------------------------------------ catalogue
  async function loadCatalogue() {
    const res = await fetch("catalogue.json", { cache: "no-store" });
    if (!res.ok) throw new Error("catalogue.json could not be loaded (" + res.status + ")");
    const cat = await res.json();
    for (const ic of cat.icons) {
      ic._search = (ic.name + " " + ic.id.replace(/_/g, " ") + " " + ic.keywords.join(" ")).toLowerCase();
      S.iconById.set(ic.id, ic);
    }
    S.catalogue = cat;
  }

  function filteredIcons() {
    const q = S.iconSearch.trim().toLowerCase();
    const terms = q ? q.split(/\s+/) : [];
    const cat = S.category;
    const out = [];
    for (const ic of S.catalogue.icons) {
      if (cat !== "all" && !ic.categories.includes(cat)) continue;
      if (terms.length && !terms.every(t => ic._search.includes(t))) continue;
      out.push(ic);
    }
    if (terms.length) {
      // names that start with the query first, then contain it, then keyword-only matches
      const score = ic => { const n = ic.name.toLowerCase(); return n.startsWith(q) ? 0 : n.includes(q) ? 1 : 2; };
      out.sort((a, b) => score(a) - score(b) || a.name.localeCompare(b.name));
    }
    return out;
  }

  // ------------------------------------------------------------------ rendering: frame
  /* A step is "active" whenever you are standing on it, and "done" once its value is chosen.
     Both at once when you go back a screen - the badge keeps the tick and the row keeps the highlight. */
  function stepState(done, screen) {
    const active = S.screen === screen;
    return done ? (active ? "done active" : "done") : (active ? "active" : "todo");
  }

  function renderStepper() {
    const st = $("#stepper");
    st.innerHTML = "";
    const steps = [];
    const sol = S.solution, tbl = S.table, ic = S.icon;
    steps.push({
      badge: sol ? ICONS.check : "1", label: "Solution",
      value: sol ? sol.name : "Select a solution",
      sub: sol ? `Publisher: ${sol.publisherName} (${sol.prefix}_)` : "unmanaged solutions only",
      state: stepState(!!sol, "solutions"),
      go: () => goSolutions()
    });
    steps.push({
      badge: tbl ? ICONS.check : "2", label: "Table",
      value: tbl ? tbl.displayName : (sol ? "Select a table" : "—"),
      sub: tbl ? tbl.logicalName : (sol ? "custom tables only" : "custom tables in the solution"), mono: !!tbl,
      state: stepState(!!tbl, "tables"),
      go: () => goTables()
    });
    steps.push({
      badge: "3", label: "Icon",
      value: ic ? ic.name : "—",
      sub: ic ? `Regular · ${ic.size}${ic.size === 20 ? " resized to 16" : ""} · selected` : "Fluent UI · Regular · 16",
      state: stepState(false, "browse")
    });
    for (const s of steps) {
      const badge = el("div", { class: "step-badge" });
      if (typeof s.badge === "string" && s.badge.startsWith("<svg")) badge.innerHTML = s.badge; else badge.textContent = s.badge;
      st.appendChild(el("div", { class: "step " + s.state },
        badge,
        el("div", { class: "step-body" },
          el("div", { class: "step-label" }, s.label),
          el("div", { class: "step-value", title: s.value }, s.value),
          el("div", { class: "step-sub" + (s.mono ? " mono" : ""), title: s.sub }, s.sub)),
        s.state.indexOf("done") === 0 ? el("button", { class: "step-change", title: "Go back to this step and choose again", onclick: s.go }, svgNode(ICONS.edit), "Change") : null
      ));
    }
  }

  function renderFooter() {
    $("#ftrVersion").textContent = "v" + (S.context.version || "0.0.0");
    $("#ftrYear").textContent = new Date().getFullYear();
    const pill = $("#connPill");
    pill.classList.toggle("on", !!S.context.connected);
    $("#connText").textContent = S.context.connected ? (S.context.orgUrl || S.context.orgName || "Connected") : "Not connected";
    $("#btnReload").disabled = !S.context.connected;
  }

  function render() {
    renderStepper();
    renderFooter();
    const body = $("#body");
    body.innerHTML = "";
    switch (S.screen) {
      case "loading": body.appendChild(emptyState(el("div", { class: "spinner" }), "Loading icon library…", "")); break;
      case "disconnected": body.appendChild(emptyState(svgNode(ICONS.grid), "Connect to an environment", "Use the XrmToolBox connection control to connect to a Dataverse environment. The solution list loads on connect.")); break;
      case "solutions": body.appendChild(renderSolutions()); break;
      case "tables": body.appendChild(renderTables()); break;
      case "browse": body.appendChild(renderBrowse()); break;
    }
    renderModal();
  }

  function emptyState(glyph, title, text, action) {
    return el("div", { class: "empty" }, el("div", { class: "empty-inner" },
      el("div", { class: "empty-glyph" }, glyph),
      el("div", { class: "empty-title" }, title),
      text ? el("div", { class: "empty-text" }, text) : null,
      action || null));
  }

  function searchBox(placeholder, value, onInput, small) {
    const input = el("input", { type: "text", placeholder, value: value || "", spellcheck: "false" });
    input.addEventListener("input", () => onInput(input.value));
    const clear = el("button", { class: "clear", title: "Clear", onclick: () => { input.value = ""; onInput(""); input.focus(); } }, "✕");
    const box = el("div", { class: "search" + (small ? " sm" : "") }, svgNode(ICONS.search, ""), input, value ? clear : null);
    box._input = input;
    return box;
  }

  // ------------------------------------------------------------------ rendering: solutions
  function renderSolutions() {
    const col = el("div", { class: "centre-col panel" }, el("span", { class: "panel-label" }, "1 · Solutions"));
    const sb = searchBox("Search solutions by name or unique name", S.solutionSearch, v => { S.solutionSearch = v; render(); focusSearch(); });
    col.appendChild(sb);
    if (S.solutionsLoading) {
      col.appendChild(el("div", { class: "list-meta" }, "Loading solutions…"));
      col.appendChild(el("div", { class: "list-box" }, el("div", { class: "list-empty" }, el("div", { class: "spinner", style: "margin:0 auto" }))));
    } else if (S.solutionsError) {
      col.appendChild(el("div", { class: "notice error" }, svgNode(ICONS.warn), el("div", null, el("strong", null, "Solutions could not be loaded. "), S.solutionsError)));
      col.appendChild(el("div", null, el("button", { class: "btn-secondary", onclick: () => loadSolutions() }, svgNode(ICONS.reload), "Try again")));
    } else if (S.solutions) {
      const q = S.solutionSearch.trim().toLowerCase();
      const rows = S.solutions.filter(s => !q || s.name.toLowerCase().includes(q) || s.uniqueName.toLowerCase().includes(q));
      col.appendChild(el("div", { class: "list-meta" }, `${fmt(S.solutions.length)} unmanaged solution${S.solutions.length === 1 ? "" : "s"} · managed solutions can't be selected and are hidden`));
      const box = el("div", { class: "list-box" });
      if (!rows.length) box.appendChild(el("div", { class: "list-empty" }, q ? "No solutions match your search." : "No unmanaged solutions found."));
      for (const s of rows) {
        box.appendChild(el("button", { class: "list-row" + (S.solution && S.solution.id === s.id ? " selected" : ""), onclick: () => selectSolution(s), ondblclick: () => { selectSolution(s); goTables(); } },
          el("div", { class: "main" }, el("div", { class: "name" }, s.name), el("div", { class: "sub" }, s.uniqueName + (s.version ? "  ·  " + s.version : ""))),
          el("div", { class: "publisher" }, s.publisherName),
          el("div", { class: "prefix" }, s.prefix + "_")));
      }
      col.appendChild(box);
      col.appendChild(el("div", { class: "list-actions" },
        el("div", { class: "hint" }, "Picking the Default Solution shows every table, but new web resources stay in the Default Solution - they won't travel with a real solution unless added later."),
        el("button", { class: "btn-primary", disabled: !S.solution, onclick: () => goTables() }, "Next: select a table", svgNode(ICONS.arrowR))));
    }
    return el("div", { class: "centre" }, col);
  }

  // ------------------------------------------------------------------ rendering: tables
  function stateGlyph(t, cls) {
    if (t.iconState === "custom" && t.webResource && t.webResource.svg) return svgNode(t.webResource.svg, cls + " custom");
    if (t.iconState === "broken") return svgNode(ICONS.warn, cls + " broken");
    return svgNode(ICONS.grid, cls);
  }
  const stateTitle = t => t.iconState === "custom" ? "Custom icon" : t.iconState === "broken" ? "Broken icon reference" : "No icon set";

  function renderTables() {
    const col = el("div", { class: "centre-col panel" }, el("span", { class: "panel-label" }, "2 · Tables"));
    col.appendChild(searchBox("Search tables by display name or logical name", S.tableSearch, v => { S.tableSearch = v; render(); focusSearch(); }));
    if (S.tablesLoading) {
      col.appendChild(el("div", { class: "list-meta" }, `Loading tables in ${S.solution.name}…`));
      col.appendChild(el("div", { class: "list-box" }, el("div", { class: "list-empty" }, el("div", { class: "spinner", style: "margin:0 auto" }))));
    } else if (S.tablesError) {
      col.appendChild(el("div", { class: "notice error" }, svgNode(ICONS.warn), el("div", null, el("strong", null, "Tables could not be loaded. "), S.tablesError)));
      col.appendChild(el("div", null, el("button", { class: "btn-secondary", onclick: () => loadTables() }, svgNode(ICONS.reload), "Try again")));
    } else if (S.tables) {
      const q = S.tableSearch.trim().toLowerCase();
      const rows = S.tables.filter(t => !q || t.displayName.toLowerCase().includes(q) || t.logicalName.toLowerCase().includes(q));
      col.appendChild(el("div", { class: "list-meta" }, `${fmt(S.tables.length)} custom table${S.tables.length === 1 ? "" : "s"} in ${S.solution.name} · system tables are hidden - their icons can't be changed`));
      const box = el("div", { class: "list-box" });
      if (!rows.length) box.appendChild(el("div", { class: "list-empty" }, q ? "No tables match your search." : "This solution contains no custom tables."));
      for (const t of rows) {
        box.appendChild(el("button", { class: "list-row" + (S.table && S.table.logicalName === t.logicalName ? " selected" : ""), onclick: () => selectTable(t), ondblclick: () => { selectTable(t); goBrowse(); } },
          stateGlyph(t, "state-icon"),
          el("div", { class: "main" }, el("div", { class: "name" }, t.displayName), el("div", { class: "sub" }, t.logicalName)),
          el("div", { class: "state-text " + t.iconState, title: t.webResource ? t.webResource.name : "" }, stateTitle(t))));
      }
      col.appendChild(box);
      col.appendChild(el("div", { class: "list-actions" },
        el("div", { class: "spacer" }),
        el("button", { class: "btn-secondary", onclick: () => goSolutions() }, svgNode(ICONS.arrowL), "Back to solutions"),
        el("button", { class: "btn-primary", disabled: !S.table, onclick: () => goBrowse() }, "Next: choose an icon", svgNode(ICONS.arrowR))));
    }
    return el("div", { class: "centre" }, col);
  }

  // ------------------------------------------------------------------ rendering: browse
  const GRID_CHUNK = 240;
  function renderBrowse() {
    const wrap = el("div", { class: "browse" });

    // categories
    const cats = el("div", { class: "cats panel" });
    const sb = searchBox("Search icons", S.iconSearch, v => { S.iconSearch = v; renderGridOnly(); }, true);
    cats.appendChild(el("div", { class: "cats-search" }, sb));
    cats.appendChild(el("div", { class: "section-label" }, "Categories"));
    const list = el("div", { class: "cats-list" });
    const all = [{ id: "all", label: "All icons", count: S.catalogue.icons.length }, ...S.catalogue.categories];
    for (const c of all) {
      list.appendChild(el("button", { class: "cat" + (S.category === c.id ? " active" : ""), onclick: () => { S.category = c.id; render(); focusSearch(); } },
        el("div", { class: "l" }, c.label), el("div", { class: "c" }, fmt(c.count))));
    }
    cats.appendChild(list);
    wrap.appendChild(cats);

    // grid
    const gw = el("div", { class: "grid-wrap panel" });
    gw.appendChild(el("span", { class: "panel-label" }, "3 · Icons"));
    gw.appendChild(el("div", { class: "grid-meta", id: "gridMeta" }));
    const scroll = el("div", { class: "grid-scroll", id: "gridScroll" });
    gw.appendChild(scroll);
    wrap.appendChild(gw);

    // rail
    wrap.appendChild(renderRail());
    setTimeout(renderGridOnly, 0);
    return wrap;
  }

  function renderGridOnly() {
    const scroll = $("#gridScroll"); const meta = $("#gridMeta");
    if (!scroll) return;
    const icons = filteredIcons();
    const catLabel = S.category === "all" ? "all categories" : (S.catalogue.categories.find(c => c.id === S.category) || {}).label;
    meta.textContent = `${fmt(icons.length)} of ${fmt(S.catalogue.icons.length)} icons · ${catLabel}${S.iconSearch.trim() ? ` · matching "${S.iconSearch.trim()}"` : ""}`;
    scroll.innerHTML = "";
    if (!icons.length) { scroll.appendChild(el("div", { class: "list-empty" }, "No icons match. Try a different word or category.")); return; }
    const grid = el("div", { class: "grid" });
    scroll.appendChild(grid);
    let rendered = 0;
    const renderChunk = () => {
      const frag = document.createDocumentFragment();
      const end = Math.min(icons.length, rendered + GRID_CHUNK);
      for (; rendered < end; rendered++) frag.appendChild(tile(icons[rendered]));
      grid.appendChild(frag);
      if (rendered < icons.length) {
        sentinel.remove(); scroll.appendChild(sentinel);
      } else sentinel.remove();
    };
    const sentinel = el("div", { class: "grid-more" }, el("div", { class: "spinner" }));
    const io = new IntersectionObserver(entries => { if (entries.some(e => e.isIntersecting)) renderChunk(); }, { root: scroll, rootMargin: "400px" });
    io.observe(sentinel);
    renderChunk();
  }

  function tile(ic) {
    const t = el("button", { class: "tile" + (S.icon && S.icon.id === ic.id ? " selected" : ""), title: `${ic.name} · Regular · ${ic.size}${ic.size === 20 ? " (resized to 16)" : ""}\n${ic.keywords.join(", ")}`, onclick: () => selectIcon(ic) },
      svgNode(ic.svg, ""), el("div", { class: "n" }, ic.name));
    return t;
  }

  function currentIconCard(t) {
    if (!t) return null;
    if (t.iconState === "custom") {
      return el("div", { class: "icon-card" }, el("div", { class: "glyph custom", html: t.webResource.svg }),
        el("div", { class: "t" }, el("div", { class: "t1" }, "Custom icon set"), el("div", { class: "t2 mono one-line", title: t.webResource.name }, t.webResource.name)));
    }
    if (t.iconState === "broken") {
      return el("div", { class: "icon-card" }, el("div", { class: "glyph broken", html: ICONS.warn }),
        el("div", { class: "t" }, el("div", { class: "t1" }, "Icon reference is broken"), el("div", { class: "t2" }, "IconVectorName points at ", el("span", { class: "mono" }, t.iconVectorName), ", which no longer exists.")));
    }
    return el("div", { class: "icon-card" }, el("div", { class: "glyph", html: ICONS.grid }),
      el("div", { class: "t" }, el("div", { class: "t1" }, "No icon set"), el("div", { class: "t2" }, "This table shows the generic default icon.")));
  }

  function renderRail() {
    const rail = el("div", { class: "rail panel" });
    const t = S.table;
    rail.appendChild(el("div", null, el("span", { class: "section-label" }, "Current icon"), currentIconCard(t)));

    const youSvg = S.icon ? S.icon.svg : (t && t.iconState === "custom" ? t.webResource.svg : ICONS.smDefault);
    const sitemap = el("div", { class: "sitemap" },
      el("div", { class: "grp" }, "Customers"),
      el("div", { class: "itm" }, svgNode(ICONS.smAccount), el("div", { class: "lbl" }, "Accounts")),
      el("div", { class: "itm" }, svgNode(ICONS.smContact), el("div", { class: "lbl" }, "Contacts")),
      el("div", { class: "grp" }, "Service"),
      el("div", { class: "itm" }, svgNode(ICONS.smCase), el("div", { class: "lbl" }, "Cases")),
      el("div", { class: "grp" }, "Your table"),
      el("div", { class: "itm you" }, svgNode(youSvg), el("div", { class: "lbl" }, t ? t.displayName : "Your table")));
    rail.appendChild(el("div", null, el("span", { class: "section-label" }, "Sitemap preview"), sitemap));

    if (S.icon) {
      rail.appendChild(el("div", null, el("span", { class: "section-label" }, "Selected icon"), iconPreview(S.icon, t)));
    }
    rail.appendChild(el("div", { class: "spacer" }));
    rail.appendChild(el("div", { class: "rail-cta" },
      el("button", { class: "btn-primary", disabled: !S.icon, onclick: () => openConfirm() }, "Review and apply", svgNode(ICONS.arrowR))));
    return rail;
  }

  function categoryLabels(ic) {
    const byId = new Map((S.catalogue.categories || []).map(c => [c.id, c.label]));
    return (ic.categories || []).map(id => byId.get(id) || id);
  }

  /* Preview of the selected icon and its catalogue metadata. */
  function iconPreview(ic, t) {
    const meta = el("div", { class: "meta" });
    const row = (k, v, clamp) => {
      meta.appendChild(el("div", { class: "k" }, k));
      meta.appendChild(el("div", { class: "v" + (clamp === "one" ? " clamp one" : clamp ? " clamp" : ""), title: v }, v));
    };
    row("Icon id", ic.id);
    row("Category", categoryLabels(ic).join(", ") || "Other", true);
    row("Keywords", (ic.keywords || []).join(", ") || "none", "one");
    return el("div", { class: "icon-preview" },
      el("div", { class: "hd" },
        el("div", { class: "glyph", html: ic.svg }),
        el("div", { class: "hd-t" },
          el("div", { class: "t1" }, ic.name),
          el("div", { class: "t2 mono" }, t ? `for ${t.logicalName}` : ic.id))),
      meta);
  }

  function focusSearch() { const i = $("#body .search input"); if (i) { i.focus(); const v = i.value; i.value = ""; i.value = v; } }

  // ------------------------------------------------------------------ navigation / data
  function goSolutions() { S.screen = "solutions"; S.modal = null; render(); focusSearch(); }
  function goTables() { if (!S.solution) return; S.screen = "tables"; S.modal = null; render(); if (!S.tables && !S.tablesLoading) loadTables(); focusSearch(); }
  function goBrowse() { if (!S.table) return; S.screen = "browse"; S.modal = null; render(); focusSearch(); }

  async function loadSolutions() {
    S.solutionsLoading = true; S.solutionsError = null; S.solutions = null;
    S.solution = null; S.tables = null; S.table = null; S.icon = null;
    // Publishers belong to the environment: a reload or a new connection must not reuse the old list.
    S.publishers = null;
    cancelTableLoad(); stopSvgWarm();
    S.screen = "solutions"; render();
    try { S.solutions = await host.call("getSolutions"); }
    catch (e) { S.solutionsError = e.message; }
    // Table metadata and the web resource index are the slow reads. Pull them now, in the background,
    // while the solution list is being read - by the time a solution is picked they are usually cached.
    if (!S.solutionsError) host.call("warmCaches").catch(() => {});
    S.solutionsLoading = false; render(); focusSearch();
  }

  function selectSolution(s) {
    // Selecting stays on this screen - "Next" moves on. Double-click is a shortcut for both.
    if (S.solution && S.solution.id === s.id) return;
    cancelTableLoad();
    S.solution = s; S.tables = null; S.table = null; S.icon = null; S.tableSearch = "";
    render(); focusSearch();
  }

  /* Background read of every SVG web resource's content, one batch per request, so the reuse check behind
     "Review and apply" finds it cached instead of reading it all while the user waits. Requests share one gate
     on the host, so small batches keep anything the user does from queuing long behind this.
     Starting again (or a reload) replaces a loop that is already running. */
  let svgWarmRun = 0;
  async function warmSvgContent() {
    const run = ++svgWarmRun;
    try {
      while (run === svgWarmRun) {
        const remaining = await host.call("warmSvgContent", { batchSize: 250 });
        if (!remaining) break;
      }
    } catch (e) { /* best effort - the confirmation panel reads whatever is left */ }
  }
  function stopSvgWarm() { svgWarmRun++; }

  /* Each table load gets a number. A reply for an older number is ignored, so changing solution while a slow
     list is loading never leaves the screen waiting on a request whose answer is thrown away. */
  let tablesRequest = 0;
  function cancelTableLoad() { tablesRequest++; S.tablesLoading = false; }

  async function loadTables() {
    const seq = ++tablesRequest;
    S.tablesLoading = true; S.tablesError = null; S.tables = null; render();
    const sol = S.solution;
    try {
      const r = await host.call("getTables", { solutionId: sol.id, solutionUniqueName: sol.uniqueName });
      if (seq !== tablesRequest) return;
      S.tables = sanitiseTables(r.tables); S.hiddenSystemCount = r.hiddenSystemCount || 0;
      warmSvgContent();
    } catch (e) {
      if (seq !== tablesRequest) return;
      S.tablesError = e.message;
    }
    S.tablesLoading = false; render(); focusSearch();
  }

  function selectTable(t) {
    if (S.table && S.table.logicalName === t.logicalName) return;
    S.table = t; S.icon = null;
    render(); focusSearch();
  }

  function selectIcon(ic) {
    S.icon = ic;
    // cheap update: swap the selected class, re-render the rail and stepper only
    document.querySelectorAll(".tile.selected").forEach(n => n.classList.remove("selected"));
    const btn = [...document.querySelectorAll(".tile")].find(n => n.title.startsWith(ic.name + " ·"));
    if (btn) btn.classList.add("selected");
    const old = $(".rail"); if (old) old.replaceWith(renderRail());
    // Bring the preview into view - the rail scrolls, and the apply button is pinned to its foot.
    const rail = $(".rail"); if (rail) rail.scrollTop = rail.scrollHeight;
    renderStepper();
  }

  // ------------------------------------------------------------------ confirmation panel
  async function openConfirm() {
    const t = S.table, ic = S.icon, sol = S.solution;
    if (!t || !ic || !sol) return;
    const m = {
      kind: "confirm", loading: true,
      // Default to updating the table's own custom icon when it has one that can be updated.
      action: (t.iconState === "custom" && t.webResource && !t.webResource.isManaged) ? "update" : "create",
      publisherId: sol.publisherId, publishers: null,
      nameRest: `${ic.id}_regular.svg`, displayName: ic.name,
      nameCheck: { state: "wait" },
      sharedTables: [], existing: [], existingChoice: null, error: null
    };
    S.modal = m; renderModal();
    try {
      const [pubs, existing, shared] = await Promise.all([
        S.publishers ? Promise.resolve(S.publishers) : host.call("getPublishers"),
        host.call("findExistingIcon", { iconId: ic.id, solutionId: sol.id, solutionUniqueName: sol.uniqueName }),
        t.iconState === "custom" ? host.call("getTablesUsingWebResource", { name: t.webResource.name }) : Promise.resolve([])
      ]);
      S.publishers = pubs;
      m.publishers = pubs; m.existing = existing || []; m.sharedTables = (shared || []).filter(x => x.logicalName !== t.logicalName);
      if (!pubs.some(p => p.id === m.publisherId)) m.publisherId = pubs.length ? pubs[0].id : null;
    } catch (e) { m.error = e.message; }
    m.loading = false; renderModal(); checkName();
  }

  const checkName = debounce(async function () {
    const m = S.modal; if (!m || m.kind !== "confirm" || m.action !== "create") return;
    const name = fullName(m);
    const v = validateName(m.nameRest);
    if (v) { m.nameCheck = { state: "bad", text: v }; renderNameCheck(); return; }
    m.nameCheck = { state: "wait", text: "checking…" }; renderNameCheck();
    try {
      const r = await host.call("checkWebResourceName", { name });
      if (fullName(S.modal) !== name) return;
      m.nameCheck = r.exists ? { state: "bad", text: r.isManaged ? "name in use (managed)" : "name already in use" } : { state: "ok", text: "name available" };
    } catch (e) { m.nameCheck = { state: "bad", text: "check failed: " + e.message }; }
    renderNameCheck();
  }, 250);

  function validateName(rest) {
    if (!rest || !rest.trim()) return "name is required";
    if (/\s/.test(rest)) return "no spaces allowed";
    if (!/^[A-Za-z0-9_.\/-]+$/.test(rest)) return "letters, numbers, . _ - and / only";
    if (/\/\//.test(rest)) return "no consecutive slashes";
    if (!/\.svg$/i.test(rest)) return "should end in .svg";
    return null;
  }
  function publisher(m) { return (m.publishers || []).find(p => p.id === m.publisherId) || null; }
  function fullName(m) { const p = publisher(m); return (p ? p.prefix + "_" : "") + m.nameRest; }
  function renderNameCheck() {
    const n = $("#nameCheck"); if (!n) return;
    const c = S.modal.nameCheck;
    n.className = "avail " + c.state;
    n.textContent = (c.state === "ok" ? "✓ " : c.state === "bad" ? "✕ " : "") + (c.text || "");
    const ok = canSubmit();
    const b1 = $("#btnSave"), b2 = $("#btnSavePublish");
    if (b1) b1.disabled = !ok; if (b2) b2.disabled = !ok;
  }
  function renderDisplayCheck() {
    const d = $("#dispCheck"); if (!d || !S.modal) return;
    const blank = !(S.modal.displayName || "").trim();
    d.className = "avail " + (blank ? "bad" : "");
    d.textContent = blank ? "✕ display name is required" : "";
  }
  function canSubmit() {
    const m = S.modal; if (!m || m.loading || m.error) return false;
    if (m.action === "create") return m.nameCheck.state === "ok" && !!publisher(m) && !!(m.displayName || "").trim();
    if (m.action === "reuse") return !!m.existingChoice;
    return true;
  }

  function renderConfirm(m) {
    const t = S.table, ic = S.icon, sol = S.solution;
    const body = el("div", { class: "modal-body panelled confirm" + (m.loading ? " loading" : "") });
    if (m.loading) {
      body.appendChild(el("div", { style: "display:flex;align-items:center;gap:10px;color:var(--muted)" }, el("div", { class: "spinner" }), "Checking publishers and existing web resources…"));
    } else if (m.error) {
      body.appendChild(el("div", { class: "notice error" }, svgNode(ICONS.warn), el("div", null, el("strong", null, "Could not prepare the confirmation. "), m.error)));
    } else {
      const canUpdate = t.iconState === "custom" && t.webResource && !t.webResource.isManaged;
      const updateReason = t.iconState === "custom" ? (t.webResource.isManaged ? "current icon is managed" : null) : "no existing custom icon";
      const hasExisting = m.existing.length > 0;

      // Step 1: action
      const radios = el("div", { class: "radios" },
        radio(m, "create", "Create new web resource", null),
        radio(m, "update", "Update existing web resource", updateReason, !canUpdate),
        hasExisting ? radio(m, "reuse", "Reuse an existing web resource with this icon", `${m.existing.length} found`) : null);
      let n = 1; // step numbers depend on the action
      const s1 = el("div", { class: "cstep on" }, el("div", { class: "cstep-n" }, String(n++)), el("div", { class: "cstep-b" }, el("div", { class: "cstep-t" }, "Choose the action"), radios));
      if (m.action === "update" && m.sharedTables.length) {
        s1.lastChild.appendChild(el("div", { class: "notice warn", style: "margin-top:8px" }, svgNode(ICONS.warn), el("div", null,
          el("strong", null, `${m.sharedTables.length} other table${m.sharedTables.length === 1 ? "" : "s"} use${m.sharedTables.length === 1 ? "s" : ""} `), el("span", { class: "mono" }, t.webResource.name), ". Updating it changes their icon too.",
          el("div", { class: "tbl-list" }, m.sharedTables.map(x => el("span", { title: x.displayName }, x.logicalName))))));
      }
      if (m.action === "update") {
        s1.lastChild.appendChild(el("div", { class: "hint", style: "margin-top:8px" }, "The SVG content of ", el("span", { class: "mono" }, t.webResource.name), " is replaced. The table keeps pointing at the same web resource."));
        s1.lastChild.appendChild(el("div", { class: "notice warn", style: "margin-top:8px" }, svgNode(ICONS.warn), el("div", null,
          el("strong", null, "Check the name still fits the icon. "),
          "An update changes the content but not the name or display name, so ", el("span", { class: "mono" }, t.webResource.name),
          " will keep describing the old icon. If it no longer matches ", el("strong", null, ic.name),
          ", go back and create a new web resource instead.")));
      }
      body.appendChild(s1);

      if (m.action === "reuse") {
        // Separate step to pick which existing web resource to reuse.
        const list = el("div", { class: "radios" });
        for (const w of m.existing) {
          list.appendChild(el("button", { class: "radio-row" + (m.existingChoice === w.id ? " on" : ""), onclick: () => { m.existingChoice = w.id; renderModal(); } },
            el("div", { class: "rb" }), el("div", { class: "rl mono", style: "font-size:11px" }, w.name), el("div", { class: "rr" }, (w.isManaged ? "managed · " : "") + (w.inSolution ? "already in solution" : "will be added to solution"))));
        }
        body.appendChild(el("div", { class: "cstep" }, el("div", { class: "cstep-n" }, String(n++)), el("div", { class: "cstep-b" },
          el("div", { class: "cstep-t" }, "Choose the existing web resource"),
          list,
          el("div", { class: "hint", style: "margin-top:8px" }, "These web resources already contain exactly this Fluent icon. Reusing one avoids a duplicate; it is added to the selected solution if it isn't there already."))));
      }

      if (m.action === "create") {
        // Step 2: publisher
        const sel = el("select", { onchange: ev => { m.publisherId = ev.target.value; renderModal(); checkName(); } });
        for (const p of m.publishers) sel.appendChild(el("option", { value: p.id, selected: p.id === m.publisherId }, `${p.name}  (prefix ${p.prefix}_)`));
        const pub = publisher(m);
        const mismatch = pub && pub.id !== sol.publisherId;
        const s2 = el("div", { class: "cstep" }, el("div", { class: "cstep-n" }, String(n++)), el("div", { class: "cstep-b" },
          el("div", { class: "cstep-t" }, "Confirm the publisher"),
          el("div", { class: "field" }, sel),
          el("div", { class: "hint", style: "margin-top:6px" }, "Pre-filled from the solution's publisher. The prefix below follows this choice."),
          mismatch ? el("div", { class: "notice warn", style: "margin-top:8px" }, svgNode(ICONS.warn), el("div", null, el("strong", null, "Prefix won't match the solution's publisher. "), `${sol.name} uses ${sol.publisherName} (${sol.prefix}_). This is allowed but easy to do by accident.`)) : null));
        body.appendChild(s2);

        // Step 3: name
        const input = el("input", { type: "text", value: m.nameRest, spellcheck: "false" });
        input.addEventListener("input", () => { m.nameRest = input.value; m.nameCheck = { state: "wait", text: "" }; renderNameCheck(); checkName(); });
        const dispInput = el("input", { type: "text", value: m.displayName, spellcheck: "false", maxlength: "200", id: "dispName", "aria-required": "true" });
        dispInput.addEventListener("input", () => { m.displayName = dispInput.value; renderDisplayCheck(); renderNameCheck(); });
        input.id = "logicalName"; input.setAttribute("aria-required", "true");
        const s3 = el("div", { class: "cstep" }, el("div", { class: "cstep-n" }, String(n++)), el("div", { class: "cstep-b" },
          el("div", { class: "cstep-t" }, "Name the web resource"),
          el("label", { class: "flabel", for: "logicalName" }, "Logical Name", el("span", { class: "req", title: "Required" }, " *")),
          el("div", { class: "name-row" }, el("div", { class: "field mono" }, el("span", { class: "pfx" }, pub ? pub.prefix + "_" : ""), input), el("div", { class: "avail wait", id: "nameCheck" })),
          el("div", { class: "hint", style: "margin:6px 0 14px" }, "What the table's IconVectorName points at. Suggested from the icon name. Letters, numbers, dots, underscores and forward slashes."),
          el("label", { class: "flabel", for: "dispName" }, "Display Name", el("span", { class: "req", title: "Required" }, " *")),
          el("div", { class: "name-row" }, el("div", { class: "field" }, dispInput), el("div", { class: "avail", id: "dispCheck" })),
          el("div", { class: "hint", style: "margin-top:6px" }, "What makers see in the solution and web resource list.")));
        body.appendChild(s3);
      }

      // Target solution
      const target = el("div", { class: "cstep" }, el("div", { class: "cstep-n" }, String(n++)), el("div", { class: "cstep-b" },
        el("div", { class: "cstep-t" }, "Target solution"),
        el("div", { class: "kv" }, el("div", null, el("span", null, sol.name), " ", el("span", { class: "mono", style: "font-size:10px;color:var(--faint)" }, sol.uniqueName)), el("div", { class: "k" }, "read-only")),
        sol.isDefault ? el("div", { class: "notice info", style: "margin-top:8px" }, svgNode(ICONS.info), el("div", null, el("strong", null, "Default Solution selected. "), "The web resource will only exist in the Default Solution. It won't move through Dev › Test › Pre-Prod › Prod unless someone adds it to a proper solution afterwards.")) : null));
      body.appendChild(target);
    }

    const ft = el("div", { class: "modal-ft" },
      el("div", { class: "hint" }, "Save writes the change without publishing - the icon won't appear in apps until the web resource and table are published. Save and Publish publishes only those two, never the whole solution."),
      el("button", { class: "btn-text", onclick: closeModal }, "Cancel"),
      el("button", { class: "btn-secondary btn-sm", id: "btnSave", disabled: !canSubmit(), onclick: () => runApply(false) }, "Save"),
      el("button", { class: "btn-primary btn-sm", id: "btnSavePublish", disabled: !canSubmit(), onclick: () => runApply(true) }, "Save and Publish"));

    return modalShell(`Apply icon to ${t.displayName}`, el("div", { class: "modal-sub" }, el("div", { class: "glyph", html: ic.svg }),
      el("div", null, `${ic.name} · Regular · ${ic.size} → `, el("strong", null, t.displayName), " in solution ", el("strong", null, sol.name))), body, ft);
  }

  function radio(m, value, label, right, disabled) {
    return el("button", { class: "radio-row" + (m.action === value ? " on" : ""), disabled: !!disabled, onclick: () => { m.action = value; renderModal(); if (value === "create") checkName(); } },
      el("div", { class: "rb" }), el("div", { class: "rl" }, label), right ? el("div", { class: "rr" }, right) : null);
  }

  // ------------------------------------------------------------------ apply / progress / result
  async function runApply(publish) {
    const m = S.modal, t = S.table, ic = S.icon, sol = S.solution;
    if (!canSubmit()) return;
    const req = {
      tableLogicalName: t.logicalName, tableDisplayName: t.displayName, iconId: ic.id,
      action: m.action, publish,
      solutionId: sol.id, solutionUniqueName: sol.uniqueName, isDefaultSolution: !!sol.isDefault,
      webResourceName: m.action === "create" ? fullName(m) : (m.action === "update" ? t.webResource.name : (m.existing.find(x => x.id === m.existingChoice) || {}).name),
      webResourceDisplayName: m.action === "create" ? (m.displayName || "").trim() : null,
      webResourceId: m.action === "update" ? t.webResource.id : (m.action === "reuse" ? m.existingChoice : null)
    };
    S.modal = { kind: "progress", publish, req, steps: initialSteps(req) };
    renderModal();
    let result;
    try { result = await host.call("apply", req); }
    catch (e) { result = { success: false, steps: S.modal.steps, error: e.message }; }
    S.modal = { kind: "result", publish, req, result };
    renderModal();
    if (result.success || result.tableUpdated) await refreshAfterApply();
    warmSvgContent(); // a failed apply clears the host caches; this refills them (a no-op when still warm)
  }

  function initialSteps(req) {
    const steps = [];
    if (req.action === "create") steps.push({ key: "webresource", label: "Create web resource", status: "pending", detail: req.webResourceName });
    if (req.action === "update") steps.push({ key: "webresource", label: "Update web resource", status: "pending", detail: req.webResourceName });
    if (req.action === "reuse") steps.push({ key: "webresource", label: "Verify existing web resource", status: "pending", detail: req.webResourceName });
    if (req.action !== "update") steps.push({ key: "solution", label: "Add to solution", status: "pending", detail: req.solutionUniqueName });
    steps.push({ key: "table", label: "Update table icon", status: "pending", detail: req.tableLogicalName });
    steps.push({ key: "publish", label: req.publish ? "Publish web resource and table" : "Publish", status: req.publish ? "pending" : "skipped", detail: req.publish ? "" : "not requested" });
    return steps;
  }

  host.on("progress", msg => {
    if (S.modal && S.modal.kind === "progress" && msg.steps) { S.modal.steps = msg.steps; renderModal(); }
  });

  function stepRows(steps) {
    return el("div", { class: "plist" }, steps.map(s => {
      const pi = el("div", { class: "pi" });
      if (s.status === "done") pi.innerHTML = ICONS.check;
      else if (s.status === "failed") pi.innerHTML = ICONS.x;
      else if (s.status === "running") pi.appendChild(el("div", { class: "spinner", style: "width:14px;height:14px" }));
      else pi.textContent = s.status === "skipped" ? "–" : "";
      return el("div", { class: "prow " + s.status }, pi, el("div", { class: "pt" }, s.label + (s.message ? " — " + s.message : "")), s.detail ? el("div", { class: "pm", title: s.detail }, s.detail) : null);
    }));
  }

  function renderProgress(m) {
    const body = el("div", { class: "modal-body panelled" },
      el("div", { class: "panel" }, stepRows(m.steps), el("div", { class: "hint" }, "Targeted publish only - never the whole solution.")));
    return modalShell("Applying icon…", null, body, null, true);
  }

  function renderResult(m) {
    const r = m.result, t = S.table, ic = S.icon;
    const failed = (r.steps || []).find(s => s.status === "failed");
    const body = el("div", { class: "modal-body panelled" });
    const head = el("div", { class: "panel" }), steps = el("div", { class: "panel" });
    body.appendChild(head); body.appendChild(steps);
    if (r.success) {
      head.appendChild(el("div", { class: "result-hero" }, el("div", { class: "big ok", html: ICONS.check }),
        el("div", null, el("div", { class: "t1" }, m.publish ? "Icon applied and published" : "Icon saved (not published)"), el("div", { class: "t2" }, t.logicalName))));
      head.appendChild(el("div", { class: "sel-info" }, el("div", { class: "glyph", html: ic.svg }), el("div", null, el("div", { class: "t1" }, t.displayName), el("div", { class: "t2 mono" }, m.req.webResourceName))));
      steps.appendChild(stepRows(r.steps || []));
      steps.appendChild(el("div", { class: "result-text" }, m.publish
        ? "The table list is refreshed - pick the next table to continue."
        : "The web resource and table change are saved but not published. The icon will not appear in apps until both are published. The table list is refreshed."));
    } else {
      head.appendChild(el("div", { class: "result-hero" }, el("div", { class: "big bad", html: ICONS.x }),
        el("div", null, el("div", { class: "t1" }, failed ? `${failed.label} failed` : "Apply failed"), el("div", { class: "t2" }, t.logicalName))));
      steps.appendChild(stepRows(r.steps || []));
      steps.appendChild(el("div", { class: "result-text" }, summariseFailure(r)));
      const detail = (failed && failed.error) || r.error;
      if (detail) steps.appendChild(el("div", { class: "err-detail" }, detail));
    }
    const ft = el("div", { class: "modal-ft" }, el("div", { class: "hint" }),
      el("button", { class: "btn-text", onclick: closeModal }, "Close"),
      r.success
        ? el("button", { class: "btn-primary btn-sm", onclick: () => { closeModal(); S.icon = null; goTables(); } }, "Next table", svgNode(ICONS.arrowR))
        : el("button", { class: "btn-primary btn-sm", onclick: () => { openConfirm(); } }, svgNode(ICONS.reload), "Retry"));
    return modalShell(r.success ? "Done" : "Not completed", null, body, ft);
  }

  function summariseFailure(r) {
    const done = (r.steps || []).filter(s => s.status === "done").map(s => s.label.toLowerCase());
    const notRun = (r.steps || []).filter(s => s.status === "pending").map(s => s.label.toLowerCase());
    const parts = [];
    if (done.length) parts.push("Completed: " + done.join(", ") + ".");
    else parts.push("Nothing was changed in the environment.");
    if (notRun.length) parts.push("Not attempted: " + notRun.join(", ") + ".");
    parts.push("Nothing was rolled back. Fix the cause and retry - an existing web resource is reused rather than duplicated.");
    return parts.join(" ");
  }

  async function refreshAfterApply() {
    // reload the table list for the same solution and keep the same table selected
    const sol = S.solution, logical = S.table.logicalName;
    try {
      const r = await host.call("getTables", { solutionId: sol.id, solutionUniqueName: sol.uniqueName });
      if (S.solution !== sol) return;
      S.tables = sanitiseTables(r.tables); S.hiddenSystemCount = r.hiddenSystemCount || 0;
      const t = S.tables.find(x => x.logicalName === logical);
      if (t) S.table = t;
      const old = $(".rail"); if (old) old.replaceWith(renderRail());
    } catch (e) { toast("Table list could not be refreshed: " + e.message); }
  }

  // ------------------------------------------------------------------ about
  async function openAbout() {
    S.modal = { kind: "about", loading: true };
    renderModal();
    try {
      const a = await host.call("getAbout");
      S.modal = { kind: "about", about: a };
    } catch (e) { S.modal = { kind: "about", error: e.message }; }
    renderModal();
  }

  function renderAbout(m) {
    const body = el("div", { class: "modal-body about" });

    body.appendChild(el("div", { class: "about-hd" },
      el("img", { class: "about-logo", src: "img/logo-256.png", alt: "Oliver4" }),
      el("div", { class: "about-id" },
        el("div", { class: "cap" }, "OLIVER4"),
        el("div", { class: "ttl" }, "Icon Library ", el("span", { class: "for" }, "for Dataverse")),
        el("div", { class: "ver" }, "Version " + (S.context.version || "0.0.0")),
        el("div", { class: "site" }, link("https://www.oliver4-devtools.com", "www.oliver4-devtools.com")))));

    body.appendChild(el("div", { class: "about-rule" }));

    body.appendChild(el("div", { class: "about-lead" }, "Apply a Fluent UI icon to a Dataverse table from inside XrmToolBox."));
    body.appendChild(el("div", { class: "about-note" },
      "An independent Oliver4 tool. It is not made, endorsed or supported by Microsoft. It uses the Fluent UI System Icons that Microsoft publishes under the MIT licence."));
    body.appendChild(el("div", { class: "about-note" },
      el("strong", null, "This tool writes to the connected environment."),
      " Applying an icon creates or updates an SVG web resource, points the table at it and publishes both. It reads solution, table and web resource metadata only - no record data is read or changed, and nothing else in the environment is touched."));
    body.appendChild(el("div", { class: "about-faint" },
      "Icons are the Fluent UI System Icons by Microsoft, bundled with the tool and normalised to currentColor, 16\u00d716, under 10 KB."));

    if (m.loading) body.appendChild(el("div", { class: "spinner" }));
    else if (m.error) body.appendChild(el("div", { class: "notice error" }, svgNode(ICONS.warn), el("div", null, m.error)));
    else {
      body.appendChild(el("div", { class: "about-links" },
        link("https://github.com/microsoft/fluentui-system-icons", "Fluent UI System Icons repository"),
        link("https://github.com/microsoft/fluentui-system-icons/blob/main/LICENSE", "Fluent UI System Icons MIT licence")));
      body.appendChild(el("div", { class: "about-faint" },
        "The Fluent UI System Icons are licensed by Microsoft under the MIT licence, reproduced in full below."));
      body.appendChild(el("div", { class: "licence" }, m.about.notices));
    }

    body.appendChild(el("div", { class: "about-rule" }));
    body.appendChild(el("div", { class: "about-copy" }, "\u00a9 " + new Date().getFullYear() + " Oliver4 Icon Library."));

    const ft = el("div", { class: "modal-ft" }, el("div", { class: "hint" }),
      el("button", { class: "btn-primary btn-sm", onclick: closeModal }, "Close"));
    return modalShell("About", null, body, ft);
  }
  function link(url, text) { return el("a", { href: "#", onclick: ev => { ev.preventDefault(); host.call("openUrl", { url }).catch(() => window.open(url, "_blank")); } }, text); }

  // ------------------------------------------------------------------ modal shell
  function modalShell(title, sub, body, ft, noClose) {
    const m = el("div", { class: "modal" + (S.modal && (S.modal.kind === "progress" || S.modal.kind === "result") ? " narrow" : "") + (S.modal && S.modal.kind === "about" ? " wide" : "") },
      el("div", { class: "modal-hd" }, el("div", { class: "t" }, title), noClose ? null : el("button", { class: "modal-x", onclick: closeModal, title: "Close" }, svgNode(ICONS.x))),
      sub, body, ft);
    return el("div", { class: "overlay", onclick: ev => { if (ev.target === ev.currentTarget && !noClose) closeModal(); } }, m);
  }
  function renderModal() {
    const h = $("#modalHost"); h.innerHTML = "";
    const m = S.modal; if (!m) return;
    if (m.kind === "confirm") h.appendChild(renderConfirm(m));
    else if (m.kind === "progress") h.appendChild(renderProgress(m));
    else if (m.kind === "result") h.appendChild(renderResult(m));
    else if (m.kind === "about") h.appendChild(renderAbout(m));
    if (m.kind === "confirm") { renderNameCheck(); renderDisplayCheck(); }
    const first = h.querySelector("input, select"); if (first && m.kind === "confirm" && !m.loading) { /* keep focus on the modal */ first.focus(); }
  }
  function closeModal() { if (S.modal && S.modal.kind === "progress") return; S.modal = null; renderModal(); }

  function toast(text) {
    const h = $("#toastHost"); h.innerHTML = "";
    const n = el("div", { class: "notice error toast" }, svgNode(ICONS.warn), el("div", null, text), el("button", { class: "btn-text", style: "height:auto;padding:0 4px", onclick: () => n.remove() }, "✕"));
    h.appendChild(n); setTimeout(() => n.remove(), 8000);
  }

  // ------------------------------------------------------------------ boot
  document.addEventListener("keydown", ev => { if (ev.key === "Escape" && S.modal) closeModal(); });
  $("#btnReload").addEventListener("click", () => loadSolutions());
  $("#btnAbout").addEventListener("click", () => openAbout());

  host.on("context", msg => {
    const was = S.context.connected;
    S.context = Object.assign({}, S.context, msg.context || {});
    if (S.context.connected && (!was || msg.reload)) loadSolutions();
    else if (!S.context.connected) { cancelTableLoad(); S.screen = "disconnected"; S.solutions = null; S.solution = null; S.tables = null; S.table = null; S.icon = null; S.publishers = null; S.modal = null; render(); }
    else renderFooter();
  });

  async function boot() {
    render();
    try {
      if (!host.isHosted && !window.__mockHost) {
        await new Promise((res, rej) => { const s = document.createElement("script"); s.src = "mock.js"; s.onload = res; s.onerror = () => rej(new Error("mock.js not found")); document.head.appendChild(s); });
      }
      await loadCatalogue();
      S.context = Object.assign({}, S.context, await host.call("getContext"));
      if (S.context.connected) await loadSolutions(); else { S.screen = "disconnected"; render(); }
    } catch (e) {
      S.screen = "disconnected"; render();
      $("#body").innerHTML = "";
      $("#body").appendChild(emptyState(svgNode(ICONS.warn), "The icon library could not start", e.message));
    }
  }
  boot();
})();
