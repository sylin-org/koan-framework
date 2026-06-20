/* WEB-0072 — MCP Explorer console. Renders the per-caller surface (/map.json) honestly:
   callable Verbs (with a JSON-Schema form + in-process Run), disclosed Doors (locked, with needs),
   Walls stay silent (the server omits them). No build step, no framework. */
(function () {
  "use strict";
  var base = window.KOAN_MCP_BASE || "/mcp";
  var app = document.getElementById("kx-app");
  var identityEl = document.getElementById("kx-identity");

  function el(tag, cls, text) {
    var e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text != null) e.textContent = text;
    return e;
  }

  fetch(base + "/map.json", { headers: { Accept: "application/json" }, credentials: "same-origin" })
    .then(function (r) { return r.json(); })
    .then(function (surface) { render(surface); loadAccessMap(); loadGrants(); })
    .catch(function (err) { app.className = ""; app.textContent = "Failed to load the surface: " + err; });

  function render(surface) {
    app.className = "";
    app.innerHTML = "";
    var id = surface.identity || {};
    if (id.name) identityEl.textContent = id.name;
    if (id.description) app.appendChild(el("div", "kx-entity-desc", id.description));

    // "What the agent is told" — the MCP initialize `instructions` (the LLM-facing guidance).
    if (surface.instructions) {
      var insCard = el("div", "kx-card");
      insCard.appendChild(el("div", "kx-section-title", "What the agent is told (instructions)"));
      var insBody = el("div", null, surface.instructions);
      insBody.style.whiteSpace = "pre-wrap";
      insBody.style.color = "var(--muted)";
      insCard.appendChild(insBody);
      app.appendChild(insCard);
    }

    // Connect an MCP client — only when the HTTP/SSE transport is enabled (else the console is in-process only).
    var transport = surface.transport || {};
    if (transport.httpSse) {
      var url = window.location.origin + (transport.route || "/mcp") + "/sse";
      var conn = el("div", "kx-card");
      conn.appendChild(el("div", "kx-section-title", "Connect an MCP client"));
      var row = el("div");
      row.style.display = "flex";
      row.style.gap = "0.5rem";
      row.style.alignItems = "center";
      var code = el("code", "kx-tool-name", url);
      code.style.wordBreak = "break-all";
      var copy = el("button", "kx-btn ghost", "Copy");
      copy.addEventListener("click", function () {
        if (navigator.clipboard) navigator.clipboard.writeText(url);
        copy.textContent = "Copied";
        setTimeout(function () { copy.textContent = "Copy"; }, 1500);
      });
      row.appendChild(code);
      row.appendChild(copy);
      conn.appendChild(row);
      app.appendChild(conn);
    }

    var entities = surface.entities || [];
    var customTools = surface.customTools || [];
    var doorCount = entities.reduce(function (n, e) { return n + ((e.doors && e.doors.length) || 0); }, 0);
    var toolCount = entities.reduce(function (n, e) { return n + ((e.tools && e.tools.length) || 0); }, 0) + customTools.length;

    if (doorCount > 0) {
      app.appendChild(el("div", "kx-anon",
        "Some capabilities are locked. Sign in or present the required scope to unlock them — see the dashed doors below."));
    }

    entities.forEach(function (entity) { app.appendChild(renderEntity(entity)); });

    if (customTools.length) {
      app.appendChild(el("div", "kx-section-title", "Custom tools"));
      var card = el("div", "kx-card");
      customTools.forEach(function (t) { card.appendChild(renderTool(t)); });
      app.appendChild(card);
    }

    if (toolCount === 0 && doorCount === 0) {
      app.appendChild(el("div", "kx-anon", "No capabilities are visible to you here."));
    }
  }

  function renderEntity(entity) {
    var card = el("div", "kx-card");
    card.appendChild(el("div", "kx-entity-name", entity.name));
    if (entity.description) card.appendChild(el("div", "kx-entity-desc", entity.description));
    (entity.tools || []).forEach(function (t) { card.appendChild(renderTool(t)); });
    (entity.doors || []).forEach(function (d) { card.appendChild(renderDoor(d)); });
    return card;
  }

  function renderDoor(door) {
    var box = el("div", "kx-tool kx-door");
    var head = el("div", "kx-tool-head");
    head.style.cursor = "default";
    head.appendChild(el("span", "kx-tool-name", door.name));
    head.appendChild(el("span", "kx-door-needs", "🔒 needs " + door.needs));
    box.appendChild(head);
    return box;
  }

  function renderTool(tool) {
    var box = el("div", "kx-tool");
    var head = el("div", "kx-tool-head");
    var left = el("div");
    left.appendChild(el("span", "kx-tool-name", tool.name));
    if (tool.description) {
      var d = el("div", "kx-entity-desc", tool.description);
      d.style.margin = "0.2rem 0 0";
      left.appendChild(d);
    }
    head.appendChild(left);
    var meta = tool.metadata || {};
    head.appendChild(meta.isMutation ? el("span", "kx-badge mut", "mutation") : el("span", "kx-badge", "read"));
    box.appendChild(head);

    var form = el("div", "kx-form");
    var fields = buildForm(tool.inputSchema || {});
    form.appendChild(fields.node);

    var run = el("button", "kx-btn", "Run");
    var runRow = el("div");
    runRow.style.marginTop = "0.5rem";
    runRow.appendChild(run);
    form.appendChild(runRow);

    var result = el("div", "kx-result");
    form.appendChild(result);

    head.addEventListener("click", function () { form.classList.toggle("open"); });
    run.addEventListener("click", function (ev) {
      ev.stopPropagation();
      var args = fields.collect();
      run.disabled = true;
      run.textContent = "Running…";
      callTool(tool.name, args)
        .then(function (res) { showResult(result, res); })
        .catch(function (err) { result.style.display = "block"; result.className = "kx-result err"; result.textContent = String(err); })
        .finally(function () { run.disabled = false; run.textContent = "Run"; });
    });

    box.appendChild(form);
    return box;
  }

  function buildForm(schema) {
    var node = el("div");
    var props = (schema && schema.properties) || {};
    var required = (schema && schema.required) || [];
    var inputs = {};
    Object.keys(props).forEach(function (key) {
      var p = props[key] || {};
      var field = el("div", "kx-field");
      var label = el("label");
      label.textContent = key;
      if (required.indexOf(key) >= 0) label.appendChild(el("span", "req", " *"));
      field.appendChild(label);

      var input;
      if (Array.isArray(p.enum)) {
        input = el("select");
        p.enum.forEach(function (v) {
          var o = el("option", null, String(v));
          o.value = String(v);
          input.appendChild(o);
        });
      } else if (p.type === "boolean") {
        input = el("input"); input.type = "checkbox";
      } else if (p.type === "integer" || p.type === "number") {
        input = el("input"); input.type = "number";
      } else if (p.type === "object" || p.type === "array") {
        input = el("textarea"); input.rows = 3; input.placeholder = p.type === "array" ? "[ ... ]" : "{ ... }";
      } else {
        input = el("input"); input.type = "text";
      }
      if (p.description) { input.title = p.description; label.title = p.description; }
      field.appendChild(input);
      inputs[key] = { input: input, schema: p };
      node.appendChild(field);
    });

    function collect() {
      var args = {};
      Object.keys(inputs).forEach(function (key) {
        var entry = inputs[key];
        var p = entry.schema;
        if (p.type === "boolean") { if (entry.input.checked) args[key] = true; return; }
        var raw = entry.input.value;
        if (raw === "" || raw == null) return;
        if (p.type === "integer") args[key] = parseInt(raw, 10);
        else if (p.type === "number") args[key] = parseFloat(raw);
        else if (p.type === "object" || p.type === "array") { try { args[key] = JSON.parse(raw); } catch (e) { args[key] = raw; } }
        else args[key] = raw;
      });
      return args;
    }

    return { node: node, collect: collect };
  }

  function callTool(name, args) {
    return fetch(base + "/explorer/call", {
      method: "POST",
      credentials: "same-origin",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify({ name: name, arguments: args })
    }).then(async function (r) {
      var text = await r.text();
      var body;
      try { body = JSON.parse(text); } catch (e) { body = { raw: text }; }
      return { status: r.status, body: body };
    });
  }

  function showResult(node, res) {
    node.style.display = "block";
    var b = res.body || {};
    if (res.status === 401) {
      node.className = "kx-result err";
      node.textContent = "Sign in to run this tool. (" + (b.message || "authentication required") + ")";
      return;
    }
    if (b.shortCircuit) {
      node.className = "kx-result err";
      node.textContent = "Denied / short-circuit:\n" + pretty(b.shortCircuit);
      return;
    }
    if (b.errorCode || b.errorMessage) {
      node.className = "kx-result err";
      node.textContent = (b.errorCode || "error") + ": " + (b.errorMessage || "");
      return;
    }
    node.className = "kx-result ok";
    node.textContent = pretty(b.payload);
  }

  function pretty(v) { try { return JSON.stringify(v, null, 2); } catch (e) { return String(v); } }

  // WEB-0072 D5: the privileged access map (god-view). Served only to Development / an admin caller — a 404 here
  // is the fail-closed "not privileged" case, so omit the section silently.
  function loadAccessMap() {
    fetch(base + "/access-map.json", { headers: { Accept: "application/json" }, credentials: "same-origin" })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (map) { if (map) renderAccessMap(map); })
      .catch(function () { /* not privileged — silently omit */ });
  }

  function renderAccessMap(map) {
    app.appendChild(el("div", "kx-section-title", "Capability access map (governance)"));
    var card = el("div", "kx-card");

    var dl = el("button", "kx-btn ghost", "Download JSON");
    dl.style.marginBottom = "0.75rem";
    dl.addEventListener("click", function () { downloadJson("access-map.json", map); });
    card.appendChild(dl);

    var table = el("table", "kx-table");
    var head = el("tr");
    ["Capability", "read", "write", "remove"].forEach(function (h) { head.appendChild(el("th", null, h)); });
    table.appendChild(head);

    (map.entities || []).forEach(function (e) {
      var tr = el("tr");
      tr.appendChild(el("td", "kx-tool-name", e.name));
      var a = e.access || {};
      ["read", "write", "remove"].forEach(function (k) { tr.appendChild(el("td", null, a[k] || "—")); });
      table.appendChild(tr);
    });
    (map.customTools || []).forEach(function (t) {
      var tr = el("tr");
      tr.appendChild(el("td", "kx-tool-name", t.name));
      var td = el("td", null, t.requirement || "—");
      td.colSpan = 3;
      tr.appendChild(td);
      table.appendChild(tr);
    });

    card.appendChild(table);
    app.appendChild(card);
  }

  function downloadJson(name, obj) {
    var blob = new Blob([JSON.stringify(obj, null, 2)], { type: "application/json" });
    var url = URL.createObjectURL(blob);
    var a = document.createElement("a");
    a.href = url;
    a.download = name;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  }

  // WEB-0072 P3 — grant exercisers. Shown only when the embedded OAuth AS (SEC-0006) is present (RFC 8414
  // discovery probes 200). A 404 means no AS — omit silently (the console still works standalone).
  function loadGrants() {
    fetch("/.well-known/oauth-authorization-server", { headers: { Accept: "application/json" } })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (meta) { if (meta) renderGrants(meta); })
      .catch(function () { /* no AS — omit */ });
  }

  function renderGrants(meta) {
    app.appendChild(el("div", "kx-section-title", "Authenticate — exercise the OAuth grants"));
    var card = el("div", "kx-card");

    // Dev token — the quick path (Development-only; the endpoint 404s elsewhere).
    var devBtn = el("button", "kx-btn ghost", "Mint a dev token");
    var devOut = el("div", "kx-result");
    devBtn.addEventListener("click", function () {
      devBtn.disabled = true;
      fetch("/oauth/dev-token", { credentials: "same-origin", headers: { Accept: "application/json" } })
        .then(async function (r) { return { status: r.status, body: await r.text() }; })
        .then(function (res) {
          devOut.style.display = "block";
          if (res.status !== 200) {
            devOut.className = "kx-result err";
            devOut.textContent = "dev-token unavailable (" + res.status + "). Sign in first; it is Development-only.";
            return;
          }
          devOut.className = "kx-result ok";
          try { var j = JSON.parse(res.body); devOut.textContent = "Bearer token (expires_in " + j.expires_in + "s):\n" + j.access_token; }
          catch (e) { devOut.textContent = res.body; }
        })
        .finally(function () { devBtn.disabled = false; });
    });
    var devRow = el("div");
    devRow.style.marginBottom = "0.75rem";
    devRow.appendChild(devBtn);
    card.appendChild(devRow);
    card.appendChild(devOut);

    // The full OAuth grants (device / auth-code + PKCE / refresh) are driven by a real MCP client — the client
    // is the OAuth actor (it registers via DCR, requests scope + resource, and runs the consent round-trip).
    // The console hands off rather than re-implementing the dance.
    var note = el("div", "kx-anon",
      "For the full OAuth flow (device / auth-code + PKCE / refresh), connect a real MCP client — it drives the grant (DCR + scope + resource + consent). Use the Connect URL above.");
    note.style.marginTop = "0.75rem";
    note.style.marginBottom = "0";
    card.appendChild(note);

    app.appendChild(card);
  }

})();
