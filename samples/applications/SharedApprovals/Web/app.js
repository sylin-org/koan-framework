const kind = document.body.dataset.kind;
const purchases = kind === "purchases";
const api = `/api/${kind}`;
const form = document.querySelector("#request-form");
const error = document.querySelector("#error");
const notice = document.querySelector("#notice");
const money = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" });
let limit;

async function request(path, options = {}) {
  const response = await fetch(path, { ...options, headers: { "Content-Type": "application/json", ...options.headers } });
  const text = await response.text();
  let body;
  try { body = text ? JSON.parse(text) : null; } catch { body = null; }
  if (!response.ok) throw new Error(body?.detail || body?.title || `The request could not be completed (${response.status}).`);
  return body;
}

function element(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

function showError(reason) {
  error.textContent = reason.message;
  error.hidden = false;
}

async function refresh() {
  error.hidden = true;
  const policy = await request("/api/approval-policy");
  limit = policy.maximumApprovalAmount;
  document.querySelector("#policy").textContent = `Requests up to ${money.format(limit)} can be approved. Approved details are final.`;
  const result = await request(`${api}?page=1&pageSize=20`);
  const rows = Array.isArray(result) ? result : result.items ?? result.data ?? [];
  const list = document.querySelector("#requests");
  list.replaceChildren();
  document.querySelector("#count").textContent = rows.length;
  document.querySelector("#empty").hidden = rows.length !== 0;
  for (const row of rows) {
    const approved = row.state === "Approved" || row.state === 1;
    const complete = purchases ? Boolean(row.orderNumber) : Boolean(row.reimbursedAt);
    const card = element("article", "card");
    const top = element("div", "card-top");
    top.append(element("h3", "", row.subject), element("span", "amount", money.format(row.amount)));
    const detail = purchases ? `${row.supplier} · ${row.costCenter}` : `${row.employee} · Receipt ${row.receiptNumber}`;
    const extra = complete ? (purchases ? ` · ${row.orderNumber}` : ` · Reimbursed ${new Date(row.reimbursedAt).toLocaleDateString()}`) : "";
    const bottom = element("div", "card-bottom");
    bottom.append(element("span", `badge ${complete ? "complete" : approved ? "approved" : ""}`,
      complete ? (purchases ? "ORDER PLACED" : "REIMBURSED") : approved ? "APPROVED" : "AWAITING APPROVAL"));
    if (!complete) {
      const overLimit = !approved && row.amount > limit;
      const action = element("button", "", overLimit ? "Above approval limit" : approved ? (purchases ? "Place order ↗" : "Reimburse ↗") : "Approve ✓");
      action.type = "button";
      action.disabled = overLimit;
      action.addEventListener("click", async () => {
        action.disabled = true;
        notice.hidden = true;
        try {
          const operation = approved ? (purchases ? "order" : "reimburse") : "approve";
          await request(`${api}/${encodeURIComponent(row.id)}/${operation}`, { method: "POST" });
          await refresh();
          notice.textContent = approved ? (purchases ? "Order recorded." : "Reimbursement recorded.") : "Approval recorded.";
          notice.hidden = false;
        } catch (reason) { showError(reason); action.disabled = false; }
      });
      bottom.append(action);
    }
    card.append(top, element("p", "details", detail + extra), bottom);
    list.append(card);
  }
}

form.addEventListener("submit", async event => {
  event.preventDefault();
  const button = form.querySelector("button[type=submit]");
  button.disabled = true;
  notice.hidden = true;
  const values = Object.fromEntries(new FormData(form));
  values.amount = Number(values.amount);
  try {
    await request(api, { method: "POST", body: JSON.stringify(values) });
    form.reset();
    await refresh();
    notice.textContent = purchases ? "Purchase submitted for approval." : "Expense submitted for approval.";
    notice.hidden = false;
  } catch (reason) { showError(reason); }
  finally { button.disabled = false; }
});
document.querySelector("#refresh").addEventListener("click", () => refresh().catch(showError));
refresh().catch(showError);
