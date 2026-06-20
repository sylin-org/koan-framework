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
    .then(render)
    .catch(function (err) { app.className = ""; app.textContent = "Failed to load the surface: " + err; });

  function render(surface) {
    app.className = "";
    app.innerHTML = "";
    var id = surface.identity || {};
    if (id.name) identityEl.textContent = id.name;
    if (id.description) app.appendChild(el("div", "kx-entity-desc", id.description));

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
})();
