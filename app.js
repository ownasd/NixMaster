/* ═══════════════════════════════════════════════════
   NixMaster — app.js
   Firebase REST API → Combined Traceability Dashboard
   
   Firebase Structure:
     EndToEndTraceability/{MAC_ID}/AssemblyApp  ← NixTraceability writes
     EndToEndTraceability/{MAC_ID}/PackingApp   ← NixPackTrace writes
═══════════════════════════════════════════════════ */

'use strict';

// ─── State ────────────────────────────────────────────────────────────────────
const state = {
  records: [],          // Combined records array
  filtered: [],         // After filter applied
  settings: {
    firebaseUrl: '',
    nodePath: 'EndToEndTraceability',
    refreshInterval: 30,
    maxRecords: 500,
  },
  autoRefreshTimer: null,
};

// ─── Init ─────────────────────────────────────────────────────────────────────
document.addEventListener('DOMContentLoaded', () => {
  loadSettings();
  startClock();
  setDefaultDateFilters();
  fetchAllRecords();
});

// ─── CLOCK ────────────────────────────────────────────────────────────────────
function startClock() {
  const el = document.getElementById('liveClock');
  const tick = () => {
    const now = new Date();
    el.textContent = now.toLocaleString('en-IN', {
      day: '2-digit', month: 'short', year: 'numeric',
      hour: '2-digit', minute: '2-digit', second: '2-digit',
      hour12: true,
    });
  };
  tick();
  setInterval(tick, 1000);
}

// ─── PAGE NAVIGATION ──────────────────────────────────────────────────────────
function showPage(pageId, linkEl) {
  document.querySelectorAll('.page').forEach(p => p.classList.remove('active'));
  document.querySelectorAll('.nav-link').forEach(l => l.classList.remove('active'));
  document.getElementById('page-' + pageId).classList.add('active');
  if (linkEl) linkEl.classList.add('active');

  const titles = {
    dashboard: ['Dashboard', 'Overview of all stations'],
    records:   ['All Records', 'Full traceability log with filters'],
    search:    ['Search Unit ID', 'Lookup individual unit trace'],
    settings:  ['Settings', 'Configure Firebase connection'],
  };
  document.getElementById('pageTitle').textContent       = titles[pageId][0];
  document.getElementById('pageBreadcrumb').textContent  = titles[pageId][1];
}

// ─── SETTINGS ─────────────────────────────────────────────────────────────────
function loadSettings() {
  try {
    const saved = JSON.parse(localStorage.getItem('nixmaster_settings') || '{}');
    Object.assign(state.settings, saved);
  } catch {}

  document.getElementById('cfgFirebaseUrl').value        = state.settings.firebaseUrl;
  document.getElementById('cfgNodePath').value           = state.settings.nodePath;
  document.getElementById('cfgRefreshInterval').value    = state.settings.refreshInterval;
  document.getElementById('cfgMaxRecords').value         = state.settings.maxRecords;
}

function saveSettings() {
  state.settings.firebaseUrl       = document.getElementById('cfgFirebaseUrl').value.trim().replace(/\/$/, '');
  state.settings.nodePath          = document.getElementById('cfgNodePath').value.trim() || 'EndToEndTraceability';
  state.settings.refreshInterval   = parseInt(document.getElementById('cfgRefreshInterval').value) || 30;
  state.settings.maxRecords        = parseInt(document.getElementById('cfgMaxRecords').value) || 500;

  localStorage.setItem('nixmaster_settings', JSON.stringify(state.settings));
  showSettingsMsg('✅ Settings saved successfully!', 'success');
  resetAutoRefresh();
  fetchAllRecords();
}

function showSettingsMsg(msg, type) {
  const el = document.getElementById('settingsMsg');
  el.textContent = msg;
  el.className = 'settings-msg ' + type;
  setTimeout(() => el.className = 'settings-msg hidden', 3000);
}

async function testConnection() {
  const url = document.getElementById('cfgFirebaseUrl').value.trim().replace(/\/$/, '');
  const node = document.getElementById('cfgNodePath').value.trim() || 'EndToEndTraceability';
  if (!url) { showSettingsMsg('❌ Enter Firebase URL first', 'error'); return; }
  try {
    const res = await fetch(`${url}/${node}.json?shallow=true`);
    if (res.ok) {
      showSettingsMsg(`✅ Connected! Firebase responded ${res.status}.`, 'success');
    } else {
      showSettingsMsg(`⚠️ Firebase error: ${res.status} ${res.statusText}`, 'error');
    }
  } catch (e) {
    showSettingsMsg(`❌ Cannot connect: ${e.message}`, 'error');
  }
}

// ─── AUTO REFRESH ─────────────────────────────────────────────────────────────
function resetAutoRefresh() {
  if (state.autoRefreshTimer) clearInterval(state.autoRefreshTimer);
  state.autoRefreshTimer = setInterval(fetchAllRecords, state.settings.refreshInterval * 1000);
}

// ─── FETCH FIREBASE DATA ──────────────────────────────────────────────────────
async function fetchAllRecords() {
  const { firebaseUrl, nodePath } = state.settings;

  if (!firebaseUrl) {
    showToast('⚙️ Please configure Firebase URL in Settings first.', 4000);
    updateSyncStatus('error');
    return;
  }

  setLoading(true);
  setRefreshAnim(true);

  try {
    // Fetch the full node — Firebase returns all MAC IDs with their children
    const url = `${firebaseUrl}/${nodePath}.json`;
    const response = await fetch(url, { cache: 'no-store' });

    if (!response.ok) throw new Error(`HTTP ${response.status}: ${response.statusText}`);

    const data = await response.json();

    if (!data || typeof data !== 'object') {
      state.records = [];
      state.filtered = [];
      renderAll();
      showToast('ℹ️ No records found in Firebase.', 3000);
      updateSyncStatus('ok');
      return;
    }

    // Parse: data = { "AA:BB:CC:...": { AssemblyApp: {...}, PackingApp: {...} }, ... }
    const records = [];
    for (const [macId, nodeData] of Object.entries(data)) {
      if (!nodeData || typeof nodeData !== 'object') continue;
      const assembly = nodeData['AssemblyApp'] || null;
      const packing  = nodeData['PackingApp']  || null;
      records.push({ macId, assembly, packing });
    }

    // Sort by assembly timestamp (newest first)
    records.sort((a, b) => {
      const tA = a.assembly?.Timestamp || a.assembly?.timestamp || '';
      const tB = b.assembly?.Timestamp || b.assembly?.timestamp || '';
      return tB.localeCompare(tA);
    });

    state.records = records;
    state.filtered = [...records];

    renderAll();
    updateSyncStatus('ok');
    document.getElementById('lastRefresh').textContent = 'Updated: ' + new Date().toLocaleTimeString('en-IN');
    resetAutoRefresh();
    showToast(`✅ ${records.length} records loaded.`, 2000);

  } catch (err) {
    console.error(err);
    updateSyncStatus('error');
    showToast(`❌ Firebase Error: ${err.message}`, 5000);
  } finally {
    setLoading(false);
    setRefreshAnim(false);
  }
}

// ─── RENDER ALL VIEWS ─────────────────────────────────────────────────────────
function renderAll() {
  renderKPIs();
  renderChart();
  renderDonut();
  renderDashTable();
  renderAllTable();
}

// ─── KPI RENDER ───────────────────────────────────────────────────────────────
function renderKPIs() {
  const total    = state.filtered.length;
  const packed   = state.filtered.filter(r => r.packing).length;
  const pending  = total - packed;
  const assembled = total;
  const boxes    = [...new Set(state.filtered.filter(r => r.packing).map(r => r.packing.BoxNo))].length;

  animateCount('kpiTotal',     total);
  animateCount('kpiAssembled', assembled);
  animateCount('kpiPacked',    packed);
  animateCount('kpiPending',   pending);
  animateCount('kpiBoxes',     boxes);
}

function animateCount(id, target) {
  const el = document.getElementById(id);
  const start = parseInt(el.textContent) || 0;
  const dur = 600;
  const step = 16;
  const steps = dur / step;
  const inc = (target - start) / steps;
  let cur = start;
  const timer = setInterval(() => {
    cur += inc;
    if ((inc >= 0 && cur >= target) || (inc < 0 && cur <= target)) {
      cur = target;
      clearInterval(timer);
    }
    el.textContent = Math.round(cur);
  }, step);
}

// ─── BAR CHART ────────────────────────────────────────────────────────────────
function renderChart() {
  const wrap = document.getElementById('barChartWrap');

  // Group by date
  const byDate = {};
  for (const r of state.filtered) {
    const ts   = r.assembly?.Timestamp || r.assembly?.timestamp || '';
    const date = ts ? ts.substring(0, 10) : 'Unknown';
    if (!byDate[date]) byDate[date] = { assembly: 0, packing: 0 };
    byDate[date].assembly++;
    if (r.packing) byDate[date].packing++;
  }

  const keys = Object.keys(byDate).sort().slice(-14); // last 14 days
  if (keys.length === 0) {
    wrap.innerHTML = '<div class="chart-placeholder">No data to chart</div>';
    return;
  }

  const maxVal = Math.max(...keys.map(k => byDate[k].assembly), 1);
  const height = 120;

  wrap.innerHTML = keys.map(date => {
    const d    = byDate[date];
    const aH   = Math.round((d.assembly / maxVal) * height);
    const pH   = Math.round((d.packing  / maxVal) * height);
    const label = date === 'Unknown' ? '?' : date.substring(5); // MM-DD
    return `
      <div class="bar-group">
        <div class="bar-pair">
          <div class="bar bar-assembly" style="height:${aH}px" title="Assembled: ${d.assembly}"></div>
          <div class="bar bar-packing"  style="height:${pH}px" title="Packed: ${d.packing}"></div>
        </div>
        <div class="bar-label">${label}</div>
        <div class="bar-val">${d.assembly}/${d.packing}</div>
      </div>`;
  }).join('');
}

// ─── DONUT CHART ──────────────────────────────────────────────────────────────
function renderDonut() {
  const total  = state.filtered.length || 1;
  const packed = state.filtered.filter(r => r.packing).length;
  const pct    = Math.round((packed / total) * 100);

  const circ   = 2 * Math.PI * 48; // 301.6
  const packedDash  = (packed / total) * circ;
  const pendingDash = circ - packedDash;

  document.getElementById('segPacked').style.strokeDasharray  = `${packedDash} ${circ - packedDash}`;
  document.getElementById('segPending').style.strokeDasharray = `${pendingDash} ${circ}`;
  document.getElementById('segPending').style.strokeDashoffset = `-${packedDash}`;
  document.getElementById('donutPct').textContent = pct + '%';
}

// ─── DASHBOARD TABLE (last 20) ────────────────────────────────────────────────
function renderDashTable() {
  const tbody = document.getElementById('dashTableBody');
  const rows  = state.filtered.slice(0, 20);
  document.getElementById('dashRecordCount').textContent = state.filtered.length;

  if (rows.length === 0) {
    tbody.innerHTML = `<tr><td colspan="9" class="empty-row">No records found</td></tr>`;
    return;
  }

  tbody.innerHTML = rows.map((r, i) => {
    const a = r.assembly || {};
    const p = r.packing  || {};
    const isPacked = !!r.packing;

    return `<tr>
      <td>${i + 1}</td>
      <td class="mac-col">${escHtml(r.macId)}</td>
      <td>${escHtml(a.Timestamp || a.timestamp || '—')}</td>
      <td>${escHtml(a.Operator || a.operator || '—')}</td>
      <td>${escHtml(a.Shift || a.shift || '—')}</td>
      <td>${isPacked ? `<strong>${escHtml(String(p.BoxNo || '?'))}</strong>` : '—'}</td>
      <td>${isPacked ? escHtml(p.PackedAt || '—') : '—'}</td>
      <td>${isPacked ? escHtml(p.PackedBy || '—') : '—'}</td>
      <td>${isPacked
        ? `<span class="status-badge status-complete">✅ Complete</span>`
        : `<span class="status-badge status-assembled">⏳ Assembled</span>`}
      </td>
    </tr>`;
  }).join('');
}

// ─── ALL RECORDS TABLE ────────────────────────────────────────────────────────
function renderAllTable() {
  const tbody = document.getElementById('allTableBody');
  document.getElementById('allRecordCount').textContent = state.filtered.length;

  if (state.filtered.length === 0) {
    tbody.innerHTML = `<tr><td colspan="15" class="empty-row">No records found</td></tr>`;
    return;
  }

  tbody.innerHTML = state.filtered.map((r, i) => {
    const a = r.assembly || {};
    const p = r.packing  || {};
    const isPacked = !!r.packing;
    const partsCount = a.Parts ? Object.keys(a.Parts).length : 0;

    return `<tr>
      <td>${i + 1}</td>
      <td class="mac-col">${escHtml(r.macId)}</td>
      <td>${escHtml(a.StationName || a.stationName || '—')}</td>
      <td>${escHtml(a.Timestamp || a.timestamp || '—')}</td>
      <td>${escHtml(a.Operator || a.operator || '—')}</td>
      <td>${escHtml(a.Shift || a.shift || '—')}</td>
      <td>${escHtml(a.Batch || a.batch || '—')}</td>
      <td>${partsCount}</td>
      <td>${isPacked ? escHtml(String(p.BoxNo || '?')) : '—'}</td>
      <td style="font-size:11px;max-width:120px;overflow:hidden;text-overflow:ellipsis;">${escHtml(p.LongQR || '—')}</td>
      <td>${escHtml(p.ShortQR || '—')}</td>
      <td>${isPacked ? escHtml(p.PackedAt || '—') : '—'}</td>
      <td>${isPacked ? escHtml(p.PackedBy || '—') : '—'}</td>
      <td>${isPacked ? escHtml(p.StationName || '—') : '—'}</td>
      <td>${isPacked
        ? `<span class="status-badge status-complete">✅ Complete</span>`
        : `<span class="status-badge status-assembled">⏳ Assembled</span>`}
      </td>
    </tr>`;
  }).join('');
}

// ─── FILTERS ──────────────────────────────────────────────────────────────────
function setDefaultDateFilters() {
  const today = new Date().toISOString().substring(0, 10);
  const monthAgo = new Date(Date.now() - 30 * 86400000).toISOString().substring(0, 10);
  document.getElementById('filterFrom').value = monthAgo;
  document.getElementById('filterTo').value   = today;
}

function applyFilters() {
  const from      = document.getElementById('filterFrom').value;
  const to        = document.getElementById('filterTo').value;
  const status    = document.getElementById('filterStatus').value;
  const shift     = document.getElementById('filterShift').value;

  state.filtered = state.records.filter(r => {
    const a = r.assembly || {};
    const ts = (a.Timestamp || a.timestamp || '').substring(0, 10);

    if (from && ts && ts < from) return false;
    if (to   && ts && ts > to)   return false;

    if (status === 'complete'  && !r.packing)  return false;
    if (status === 'assembled' && r.packing)   return false;

    if (shift) {
      const aShift = (a.Shift || a.shift || '').toLowerCase();
      if (aShift !== shift.toLowerCase()) return false;
    }

    return true;
  });

  renderAll();
  showToast(`🔍 Filter applied: ${state.filtered.length} records shown.`, 2500);
}

function clearFilters() {
  setDefaultDateFilters();
  document.getElementById('filterStatus').value = '';
  document.getElementById('filterShift').value  = '';
  state.filtered = [...state.records];
  renderAll();
  showToast('✅ Filters cleared.', 2000);
}

// ─── SEARCH MAC ───────────────────────────────────────────────────────────────
async function searchMac() {
  const rawInput = document.getElementById('searchMacInput').value.trim();
  if (!rawInput) return;

  const { firebaseUrl, nodePath } = state.settings;
  if (!firebaseUrl) {
    showToast('⚙️ Configure Firebase URL in Settings first.', 3000);
    return;
  }

  hideEl('searchResult');
  hideEl('searchEmpty');
  hideEl('searchError');
  setLoading(true);

  try {
    // Sanitize key (same as C# SanitizeKey)
    const macKey = rawInput.replace(/#/g,'_').replace(/\$/g,'_').replace(/\[/g,'_').replace(/\]/g,'_').replace(/\//g,'_');
    const url = `${firebaseUrl}/${nodePath}/${encodeURIComponent(macKey)}.json`;
    const res = await fetch(url, { cache: 'no-store' });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);

    const data = await res.json();

    if (!data || data === 'null') {
      showEl('searchEmpty');
      return;
    }

    const assembly = data['AssemblyApp'] || null;
    const packing  = data['PackingApp']  || null;

    // Assembly card
    document.getElementById('assemblyStatus').textContent  = assembly ? '✅ Found' : '❌ Not Found';
    document.getElementById('assemblyStatus').style.color  = assembly ? 'var(--success)' : 'var(--danger)';

    document.getElementById('assemblyFields').innerHTML = assembly
      ? buildFields({
          'MAC ID':       rawInput,
          'Station':      assembly.StationName || assembly.stationName || '—',
          'Operator':     assembly.Operator    || assembly.operator    || '—',
          'Shift':        assembly.Shift       || assembly.shift       || '—',
          'Batch':        assembly.Batch       || assembly.batch       || '—',
          'Assembled At': assembly.Timestamp   || assembly.timestamp   || '—',
          'Record ID':    String(assembly.RecordId || '—'),
        })
      : '<div class="field-row"><span class="field-key">No assembly record found</span></div>';

    // Parts
    const partsGrid = document.getElementById('partsGrid');
    if (assembly?.Parts && Object.keys(assembly.Parts).length > 0) {
      partsGrid.innerHTML = Object.entries(assembly.Parts).map(([k, v]) =>
        `<div class="part-item"><span class="part-key">${escHtml(k)}</span><span class="part-val">${escHtml(v)}</span></div>`
      ).join('');
    } else {
      partsGrid.innerHTML = '<div class="part-item"><span class="part-key" style="color:var(--text-3)">No parts data</span></div>';
    }

    // Packing card
    document.getElementById('packingStatus').textContent = packing ? '📦 Packed' : '⏳ Not Yet';
    document.getElementById('packingStatus').style.color = packing ? 'var(--purple)' : 'var(--warning)';

    document.getElementById('packingFields').innerHTML = packing
      ? buildFields({
          'Box No':         String(packing.BoxNo || '—'),
          'Packed At':      packing.PackedAt     || '—',
          'Packed By':      packing.PackedBy     || '—',
          'Pack Station':   packing.StationName  || '—',
          'Long QR':        packing.LongQR       || '—',
          'Short QR':       packing.ShortQR      || '—',
          'Status':         packing.Status       || '—',
        })
      : '<div class="field-row"><span class="field-key">Unit not yet packed</span></div>';

    showEl('searchResult');

  } catch (err) {
    document.getElementById('searchErrorText').textContent = err.message;
    showEl('searchError');
  } finally {
    setLoading(false);
  }
}

function buildFields(obj) {
  return Object.entries(obj).map(([k, v]) =>
    `<div class="field-row">
      <span class="field-key">${escHtml(k)}</span>
      <span class="field-value">${escHtml(v)}</span>
    </div>`
  ).join('');
}

// ─── EXPORT CSV ───────────────────────────────────────────────────────────────
function exportToCSV() {
  if (state.filtered.length === 0) {
    showToast('⚠️ No records to export.', 2500);
    return;
  }

  const headers = [
    'MAC_ID','Assembly_Station','Assembly_Timestamp','Operator','Shift','Batch',
    'Parts_Count','Assembly_RecordId',
    'Box_No','Long_QR','Short_QR','Packed_At','Packed_By','Pack_Station','Pack_Status',
    'Overall_Status'
  ];

  const rows = state.filtered.map(r => {
    const a = r.assembly || {};
    const p = r.packing  || {};
    const partsCount = a.Parts ? Object.keys(a.Parts).length : 0;

    return [
      r.macId,
      a.StationName || a.stationName || '',
      a.Timestamp   || a.timestamp   || '',
      a.Operator    || a.operator    || '',
      a.Shift       || a.shift       || '',
      a.Batch       || a.batch       || '',
      partsCount,
      a.RecordId    || '',
      p.BoxNo       || '',
      p.LongQR      || '',
      p.ShortQR     || '',
      p.PackedAt    || '',
      p.PackedBy    || '',
      p.StationName || '',
      p.Status      || '',
      r.packing ? 'Complete' : 'Assembled Only',
    ].map(v => `"${String(v).replace(/"/g, '""')}"`);
  });

  const csvContent = [headers.join(','), ...rows.map(r => r.join(','))].join('\r\n');
  const blob = new Blob(['\uFEFF' + csvContent], { type: 'text/csv;charset=utf-8;' });
  const link = document.createElement('a');
  link.href = URL.createObjectURL(blob);
  link.download = `NixMaster_Traceability_${new Date().toISOString().substring(0,10)}.csv`;
  link.click();
  URL.revokeObjectURL(link.href);
  showToast(`⬇ Exported ${state.filtered.length} records as CSV.`, 3000);
}

// ─── HELPERS ──────────────────────────────────────────────────────────────────
function setLoading(on) {
  document.getElementById('loadingOverlay').classList.toggle('hidden', !on);
}

function setRefreshAnim(on) {
  const icon = document.getElementById('refreshIcon');
  icon.style.animation = on ? 'spin 0.6s linear infinite' : '';
}

function updateSyncStatus(state) {
  const el  = document.getElementById('syncStatus');
  const dot = el.querySelector('.dot');
  dot.className = 'dot ' + (state === 'ok' ? 'dot-green' : state === 'error' ? 'dot-red' : 'dot-orange');
  el.childNodes[1].textContent = state === 'ok' ? ' Live Connected' : state === 'error' ? ' Connection Error' : ' Connecting...';
}

function showToast(msg, duration = 3000) {
  const el = document.getElementById('toast');
  el.textContent = msg;
  el.classList.remove('hidden');
  clearTimeout(el._timer);
  el._timer = setTimeout(() => el.classList.add('hidden'), duration);
}

function showEl(id) { document.getElementById(id).classList.remove('hidden'); }
function hideEl(id) { document.getElementById(id).classList.add('hidden'); }

function escHtml(str) {
  return String(str)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}
