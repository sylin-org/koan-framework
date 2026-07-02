// ARCH-0104 — Koan Tenancy operator console. Vanilla JS over /api/tenancy/admin (relative to /tenancy/).
"use strict";

const API = "../api/tenancy/admin";
const $ = (id) => document.getElementById(id);
const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));

let selected = null; // currently-open tenant id
let actingAs = null; // { id, name } or null

async function api(method, path, body) {
  const res = await fetch(`${API}${path}`, {
    method,
    headers: body ? { "Content-Type": "application/json" } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!res.ok) {
    let msg = `${res.status} ${res.statusText}`;
    try { const j = await res.json(); if (j && j.error) msg = j.error; } catch { /* ignore */ }
    throw new Error(msg);
  }
  return res.status === 204 ? null : res.json();
}

function toast(msg, isError) {
  const t = $("toast");
  t.textContent = msg;
  t.className = "toast" + (isError ? " error" : "");
  t.hidden = false;
  setTimeout(() => { t.hidden = true; }, 3200);
}

// --- Fleet roster ---
async function loadFleet() {
  const roster = await api("GET", "/tenants");
  $("posture").textContent = roster.posture;
  $("posture").className = "badge " + (roster.posture === "Open" ? "warn" : "ok");
  $("operator").textContent = roster.operator ? `as ${roster.operator}` : "";
  const body = $("fleet-body");
  if (!roster.tenants.length) {
    body.innerHTML = `<tr><td colspan="5" class="muted">No tenants yet. Create one to begin.</td></tr>`;
    return;
  }
  body.innerHTML = roster.tenants.map((t) => `
    <tr data-id="${esc(t.id)}" class="${t.id === selected ? "sel" : ""}">
      <td>${esc(t.name)}</td>
      <td>${t.code ? esc(t.code) : '<span class="muted">—</span>'}</td>
      <td><span class="badge ${t.status === "Active" ? "ok" : "warn"}">${esc(t.status)}</span></td>
      <td class="num">${t.seatCount}</td>
      <td class="num">${t.pendingInvites}</td>
    </tr>`).join("");
  body.querySelectorAll("tr[data-id]").forEach((tr) => tr.addEventListener("click", () => openDetail(tr.dataset.id)));
}

// --- Tenant detail ---
async function openDetail(id) {
  selected = id;
  const d = await api("GET", `/tenants/${encodeURIComponent(id)}`);
  const t = d.tenant;
  $("detail").hidden = false;
  $("detail-name").textContent = t.name;
  $("detail-id").textContent = t.id;
  $("detail-status").textContent = t.status;
  $("detail-status").className = "badge " + (t.status === "Active" ? "ok" : "warn");

  const suspendBtn = t.status === "Active"
    ? `<button data-act="suspend">Suspend</button>`
    : `<button data-act="reactivate">Reactivate</button>`;
  $("detail-actions").innerHTML = `
    <button data-act="rename">Rename</button>
    ${suspendBtn}
    <button data-act="invite">Invite…</button>
    <button data-act="act-as">Act-as</button>
    <button class="danger" data-act="erase">Erase…</button>`;
  $("detail-actions").querySelectorAll("button").forEach((b) => b.addEventListener("click", () => action(b.dataset.act, t)));

  $("members-body").innerHTML = d.members.length ? d.members.map((m) => `
    <tr>
      <td>${esc(m.identityId)}</td>
      <td>${(m.roles || []).map((r) => `<span class="chip">${esc(r)}</span>`).join(" ") || '<span class="muted">—</span>'}</td>
      <td class="right"><button class="link danger" data-revoke-m="${esc(m.id)}">revoke</button></td>
    </tr>`).join("") : `<tr><td colspan="3" class="muted">No members.</td></tr>`;
  $("members-body").querySelectorAll("[data-revoke-m]").forEach((b) =>
    b.addEventListener("click", () => revokeMembership(b.dataset.revokeM)));

  $("invites-body").innerHTML = d.invites.length ? d.invites.map((i) => `
    <tr>
      <td>${esc(i.email)}</td><td>${esc(i.role)}</td>
      <td><span class="badge ${i.status === "Pending" ? "ok" : "warn"}">${esc(i.status)}</span></td>
      <td class="right">${i.status === "Pending" ? `<button class="link danger" data-revoke-i="${esc(i.id)}">revoke</button>` : ""}</td>
    </tr>`).join("") : `<tr><td colspan="4" class="muted">No invites.</td></tr>`;
  $("invites-body").querySelectorAll("[data-revoke-i]").forEach((b) =>
    b.addEventListener("click", () => revokeInvite(b.dataset.revokeI)));

  await loadFleet();
}

async function action(act, t) {
  try {
    if (act === "rename") {
      const name = prompt(`Rename '${t.name}' to:`, t.name);
      if (!name) return;
      await api("POST", `/tenants/${encodeURIComponent(t.id)}/rename`, { name });
      toast("Renamed.");
    } else if (act === "suspend" || act === "reactivate") {
      await api("POST", `/tenants/${encodeURIComponent(t.id)}/${act}`);
      toast(act === "suspend" ? "Suspended." : "Reactivated.");
    } else if (act === "invite") {
      const email = prompt("Invite email:");
      if (!email) return;
      const role = prompt("Role:", "member") || "member";
      await api("POST", `/tenants/${encodeURIComponent(t.id)}/invites`, { email, role });
      toast("Invite created.");
    } else if (act === "act-as") {
      const r = await api("POST", `/tenants/${encodeURIComponent(t.id)}/act-as`);
      setActingAs({ id: r.tenantId, name: r.tenantName });
      toast(`Now acting as ${r.tenantName}.`);
    } else if (act === "erase") {
      const confirmName = prompt(`ERASE '${t.name}' (control-plane). This removes all memberships & invites.\nType the tenant name to confirm:`);
      if (!confirmName) return;
      await api("POST", `/tenants/${encodeURIComponent(t.id)}/erase`, { confirm: true, confirmName });
      toast("Erase submitted.");
      $("detail").hidden = true;
      selected = null;
    }
    await refresh();
  } catch (e) {
    toast(e.message, true);
  }
}

async function revokeMembership(id) {
  if (!confirm("Revoke this membership seat?")) return;
  try { await api("POST", `/memberships/${encodeURIComponent(id)}/revoke`); toast("Seat revoked."); if (selected) await openDetail(selected); }
  catch (e) { toast(e.message, true); }
}

async function revokeInvite(id) {
  try { await api("POST", `/invites/${encodeURIComponent(id)}/revoke`); toast("Invite revoked."); if (selected) await openDetail(selected); }
  catch (e) { toast(e.message, true); }
}

// --- Act-as banner ---
function setActingAs(tenant) {
  actingAs = tenant;
  const banner = $("scope-banner");
  if (tenant) {
    $("scope-tenant").textContent = tenant.name;
    banner.hidden = false;
  } else {
    banner.hidden = true;
  }
}
$("scope-stop").addEventListener("click", async () => {
  if (!actingAs) return;
  try { await api("POST", `/tenants/${encodeURIComponent(actingAs.id)}/act-as/stop`); } catch { /* best-effort */ }
  setActingAs(null);
  toast("Stopped acting-as.");
  await refresh();
});

// --- New tenant ---
$("new-tenant").addEventListener("click", async () => {
  const name = prompt("New tenant name:");
  if (!name) return;
  const code = prompt("Routing slug (optional):") || null;
  try { const t = await api("POST", "/tenants", { name, code }); toast("Tenant created."); await refresh(); await openDetail(t.id); }
  catch (e) { toast(e.message, true); }
});
$("detail-close").addEventListener("click", () => { $("detail").hidden = true; selected = null; loadFleet(); });

// --- Feeds ---
async function loadOperations() {
  const ops = await api("GET", "/operations");
  $("operations-body").innerHTML = ops.length ? ops.map((o) => `
    <tr>
      <td>${fmt(o.requestedAt)}</td><td>${esc(o.tenantId)}</td><td>${esc(o.action)}</td>
      <td><span class="badge ${o.status === "Completed" ? "ok" : o.status === "Failed" ? "err" : "warn"}">${esc(o.status)}</span></td>
      <td>${o.status === "Completed" ? `−${o.removedMemberships} seats, −${o.removedInvites} invites` : (o.error ? esc(o.error) : "…")}</td>
      <td>${esc(o.requestedBy)}</td>
    </tr>`).join("") : `<tr><td colspan="6" class="muted">No operations.</td></tr>`;
}
async function loadAudit() {
  const rows = await api("GET", "/audit");
  $("audit-body").innerHTML = rows.length ? rows.map((a) => `
    <tr>
      <td>${fmt(a.at)}</td><td>${esc(a.actor)}</td><td>${esc(a.action)}</td>
      <td>${a.tenantId ? esc(a.tenantId) : '<span class="muted">—</span>'}</td><td>${esc(a.summary)}</td>
    </tr>`).join("") : `<tr><td colspan="5" class="muted">No audit entries.</td></tr>`;
}

document.querySelectorAll(".tab").forEach((tab) => tab.addEventListener("click", () => {
  document.querySelectorAll(".tab").forEach((t) => t.classList.remove("active"));
  tab.classList.add("active");
  const which = tab.dataset.tab;
  $("tab-operations").hidden = which !== "operations";
  $("tab-audit").hidden = which !== "audit";
  (which === "operations" ? loadOperations : loadAudit)();
}));

function fmt(iso) {
  if (!iso) return "—";
  const d = new Date(iso);
  return isNaN(d) ? esc(iso) : d.toLocaleString();
}

async function refresh() {
  await loadFleet();
  await loadOperations();
  if (!$("tab-audit").hidden) await loadAudit();
  if (selected && !$("detail").hidden) await openDetail(selected);
}

refresh().catch((e) => toast(e.message, true));
