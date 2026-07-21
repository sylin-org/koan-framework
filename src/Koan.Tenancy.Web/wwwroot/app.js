"use strict";

const API = "../api/tenancy/admin";
const $ = (id) => document.getElementById(id);
const esc = (value) => String(value ?? "").replace(/[&<>"']/g, (character) => ({
  "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;",
}[character]));

let selected = null;

async function api(method, path, body) {
  const response = await fetch(`${API}${path}`, {
    method,
    headers: body ? { "Content-Type": "application/json" } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!response.ok) {
    let message = `${response.status} ${response.statusText}`;
    try {
      const payload = await response.json();
      if (payload?.error) message = payload.error;
    } catch { /* response has no JSON error body */ }
    throw new Error(message);
  }
  return response.status === 204 ? null : response.json();
}

function toast(message, isError = false) {
  const target = $("toast");
  target.textContent = message;
  target.className = "toast" + (isError ? " error" : "");
  target.hidden = false;
  setTimeout(() => { target.hidden = true; }, 3200);
}

async function loadFleet() {
  const roster = await api("GET", "/tenants");
  $("posture").textContent = roster.posture;
  $("posture").className = "badge " + (roster.posture === "Open" ? "warn" : "ok");
  $("operator").textContent = roster.operator ? `as ${roster.operator}` : "";

  const body = $("fleet-body");
  body.innerHTML = roster.tenants.length ? roster.tenants.map((tenant) => `
    <tr data-id="${esc(tenant.id)}" class="${tenant.id === selected ? "sel" : ""}">
      <td>${esc(tenant.name)}</td>
      <td>${tenant.code ? esc(tenant.code) : '<span class="muted">—</span>'}</td>
      <td class="num">${tenant.seatCount}</td>
    </tr>`).join("") : '<tr><td colspan="3" class="muted">No tenants yet.</td></tr>';
  body.querySelectorAll("tr[data-id]").forEach((row) =>
    row.addEventListener("click", () => openDetail(row.dataset.id)));
}

async function openDetail(id) {
  selected = id;
  const detail = await api("GET", `/tenants/${encodeURIComponent(id)}`);
  $("detail").hidden = false;
  $("detail-name").textContent = detail.tenant.name;
  $("detail-id").textContent = detail.tenant.id;

  const body = $("members-body");
  body.innerHTML = detail.memberships.length ? detail.memberships.map((membership) => `
    <tr>
      <td>${esc(membership.identityId)}</td>
      <td>${(membership.roles || []).map((role) => `<span class="chip">${esc(role)}</span>`).join(" ")}</td>
      <td class="right"><button class="link danger" data-revoke="${esc(membership.id)}">revoke</button></td>
    </tr>`).join("") : '<tr><td colspan="3" class="muted">No memberships.</td></tr>';
  body.querySelectorAll("[data-revoke]").forEach((button) =>
    button.addEventListener("click", () => revokeMembership(button.dataset.revoke)));
  await loadFleet();
}

$("detail-actions").addEventListener("click", async (event) => {
  const action = event.target?.dataset?.act;
  if (!action || !selected) return;
  try {
    if (action === "rename") {
      const name = prompt("New tenant name:", $("detail-name").textContent);
      if (!name) return;
      await api("POST", `/tenants/${encodeURIComponent(selected)}/rename`, { name });
      toast("Tenant renamed.");
    }
    if (action === "grant") {
      const identityId = prompt("Known subject or durable identity ID:");
      if (!identityId) return;
      const roleInput = prompt("Comma-separated tenant roles:", "member") || "member";
      const roles = roleInput.split(",").map((role) => role.trim()).filter(Boolean);
      await api("POST", `/tenants/${encodeURIComponent(selected)}/memberships`, { identityId, roles });
      toast("Membership granted.");
    }
    await refresh();
  } catch (error) {
    toast(error.message, true);
  }
});

async function revokeMembership(id) {
  if (!confirm("Revoke this membership?")) return;
  try {
    await api("DELETE", `/memberships/${encodeURIComponent(id)}`);
    toast("Membership revoked.");
    await refresh();
  } catch (error) {
    toast(error.message, true);
  }
}

$("new-tenant").addEventListener("click", async () => {
  const name = prompt("New tenant name:");
  if (!name) return;
  const code = prompt("Routing code (optional):") || null;
  try {
    const tenant = await api("POST", "/tenants", { name, code });
    toast("Tenant created.");
    await refresh();
    await openDetail(tenant.id);
  } catch (error) {
    toast(error.message, true);
  }
});

$("detail-close").addEventListener("click", () => {
  $("detail").hidden = true;
  selected = null;
  loadFleet();
});

async function loadAudit() {
  const entries = await api("GET", "/audit");
  $("audit-body").innerHTML = entries.length ? entries.map((entry) => `
    <tr>
      <td>${formatDate(entry.at)}</td><td>${esc(entry.actor)}</td><td>${esc(entry.action)}</td>
      <td>${entry.tenantId ? esc(entry.tenantId) : '<span class="muted">—</span>'}</td>
      <td>${esc(entry.summary)}</td>
    </tr>`).join("") : '<tr><td colspan="5" class="muted">No audit entries.</td></tr>';
}

function formatDate(value) {
  if (!value) return "—";
  const date = new Date(value);
  return Number.isNaN(date.valueOf()) ? esc(value) : date.toLocaleString();
}

async function refresh() {
  await loadFleet();
  await loadAudit();
  if (selected && !$("detail").hidden) await openDetail(selected);
}

refresh().catch((error) => toast(error.message, true));
