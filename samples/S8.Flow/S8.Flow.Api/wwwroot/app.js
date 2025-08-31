console.log('S8 Flow Monitor loaded');

async function fetchJson(url) {
	const r = await fetch(url, { headers: { 'accept': 'application/json' } });
	if (!r.ok) throw new Error(`${r.status} ${r.statusText}`);
	return r.json();
}

async function refresh() {
	try {
		const health = await fetchJson('/adapters/health');
		document.getElementById('health').textContent = JSON.stringify(health, null, 2);
	} catch (e) { console.error('health', e); }

	try {
		const page = await fetchJson('/views/canonical?page=1&size=10');
		document.getElementById('canonical').textContent = JSON.stringify(page, null, 2);
	} catch (e) { console.error('views', e); }
}

refresh();
setInterval(refresh, 2000);