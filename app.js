// app.js — UI for the settler planner. Logic lives in core.js (GMCore).
(function () {
  "use strict";
  const C = window.GMCore, { SKILLS, SKILL_NAME } = C;
  const $ = s => document.querySelector(s);
  const esc = s => String(s ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
  const CAT_LABEL = { old_background: "Old background", recent_background: "Recent background", pseudonym: "Pseudonym", perk: "Perk" };
  const LVL_NAME = { 1: "Resentful", 2: "Unwilling", 3: "Indifferent", 4: "Eager", 5: "Passionate", 6: "Incapable" };
  const LVL_ICON = { 1: "✗✗", 2: "✗", 4: "♥", 5: "♥♥", 6: "⊘" };
  const STORE = "gm-planner-v6";

  // role presets: skill -> [priority, target level]
  const PRESETS = {
    Custom: null,
    "Solo generalist": { Botanical: [3, 10], Construction: [3, 10], Culinary: [2, 8], Carpentry: [2, 8], Mining: [2, 6], Medicine: [2, 6], Melee: [2, 6], Marksman: [1, 5], AnimalHandling: [1, 4], Tailoring: [1, 4], Smithing: [1, 3], Intelectual: [1, 3], Speechcraft: [1, 2] },
    Provider: { Botanical: [3, 15], Culinary: [2, 10], AnimalHandling: [1, 6], Medicine: [1, 5], Marksman: [1, 5] },
    "Builder (broad)": { Construction: [3, 15], Carpentry: [2, 12], Mining: [2, 10], Melee: [1, 6], Smithing: [1, 4] },
    Rancher: { AnimalHandling: [3, 20], Botanical: [1, 8], Culinary: [1, 6], Melee: [1, 5] },
    Gardener: { Botanical: [3, 20], AnimalHandling: [1, 8], Medicine: [1, 6], Tailoring: [1, 5] },
    Farmer: { Botanical: [3, 20], Culinary: [2, 10], AnimalHandling: [1, 6] },
    Builder: { Construction: [3, 20], Carpentry: [2, 12], Mining: [2, 10], Smithing: [1, 5], Marksman: [1, 5] },
    Miner: { Mining: [3, 20], Construction: [1, 10], Smithing: [1, 6] },
    Carpenter: { Carpentry: [3, 20], Construction: [1, 10], Art: [1, 5] },
    Cook: { Culinary: [3, 20], Botanical: [1, 10], AnimalHandling: [1, 5] },
    Doctor: { Medicine: [3, 20], Intelectual: [1, 10], Botanical: [1, 5] },
    Scholar: { Intelectual: [3, 20], Speechcraft: [1, 10], Medicine: [1, 5] },
    Smith: { Smithing: [3, 20], Mining: [1, 10], Tailoring: [1, 5] },
    Tailor: { Tailoring: [3, 20], Art: [1, 8], AnimalHandling: [1, 5] },
    Diplomat: { Speechcraft: [3, 20], Intelectual: [1, 10], Medicine: [1, 5] },
    Defender: { Melee: [3, 20], Marksman: [2, 12], Medicine: [1, 8], Construction: [1, 5] },
    "Hunter / Archer": { Marksman: [3, 20], AnimalHandling: [1, 8], Culinary: [1, 5] },
    // "best starter trio" roles
    "Master builder": { Construction: [3, 30], Carpentry: [2, 20], Mining: [2, 15], AnimalHandling: [2, 15], Culinary: [1, 8], Melee: [1, 8] },
    "Farmer-cook-medic": { Botanical: [3, 30], Culinary: [3, 25], AnimalHandling: [2, 15], Medicine: [2, 12], Tailoring: [1, 6] },
    "Scholar-archer": { Intelectual: [3, 30], Marksman: [2, 15], Culinary: [1, 10], Medicine: [1, 8], Speechcraft: [1, 8] },
  };
  const TEMPLATES = {
    "Best start: builder / farmer / scholar (3)": [["Builder", "Master builder", ["FleetFooted", "Wise"], "35-45"], ["Farmer", "Farmer-cook-medic", ["FleetFooted", "Wise"], "35-45"], ["Scholar", "Scholar-archer", ["FleetFooted", "Wise", "Erudite"], "35-45"]],
    "Solo generalist (1)": [["Survivor", "Solo generalist"]],
    "Duo: provider + builder (2)": [["Provider", "Provider"], ["Builder", "Builder (broad)"]],
    "Trio: rancher / gardener / builder (3)": [["Rancher", "Rancher"], ["Gardener", "Gardener"], ["Builder", "Builder"]],
    "Trio: farmer / builder / defender (3)": [["Farmer", "Farmer"], ["Builder", "Builder"], ["Defender", "Defender"]],
  };
  const DEFAULT_TEMPLATE = "Best start: builder / farmer / scholar (3)";

  let data, state, res = null;

  const applyPreset = (s, p) => {
    const P = PRESETS[p]; if (!P) return;
    s.preset = p; s.weights = {}; s.targets = {};
    for (const [k, [w, t]] of Object.entries(P)) { s.weights[k] = w; s.targets[k] = t; }
  };
  function newSettler(i, name, preset, perks, age) {
    const s = { name: name || `Settler ${i + 1}`, preset: "Custom", gender: i % 2 ? "Female" : "Male", age: age || "25-35", weights: {}, targets: {}, lock: { old: "", recent: "", pseudo: "" }, mustPerks: perks ? perks.slice() : [] };
    applyPreset(s, preset || "Solo generalist"); return s;
  }
  function fromTemplate(name) {
    const T = TEMPLATES[name];
    return { template: name, count: T.length, budget: 90, cap: 50, iw: 5, allowBad: true, maxBad: 1, maxPerks: 10, banned: [], settlers: T.map(([n, p, mp, a], i) => newSettler(i, n, p, mp, a)) };
  }
  function load() { try { const s = JSON.parse(localStorage.getItem(STORE)); if (s && s.settlers) return s; } catch (e) {} return fromTemplate(DEFAULT_TEMPLATE); }
  const save = () => localStorage.setItem(STORE, JSON.stringify(state));
  const team = () => state.settlers.slice(0, state.count);
  const pool = () => state.budget * state.count;

  // ---------- data loading ----------
  async function boot() {
    const files = ["all_options", "interests", "interest_levels", "generation_rules", "roles"];
    try {
      const texts = await Promise.all(files.map(f => fetch(`data/${f}.csv`).then(r => { if (!r.ok) throw new Error(`${f}.csv: HTTP ${r.status}`); return r.text(); })));
      data = C.buildData(Object.fromEntries(files.map((f, i) => [f, texts[i]])));
    } catch (e) {
      const st = $("#status"); st.classList.add("error");
      st.textContent = `Couldn't load data/*.csv (${e.message}).\nBrowsers block fetch() on file:// pages. From the going-medieval folder, run:\n    python3 -m http.server 8000\nthen open http://localhost:8000`;
      return;
    }
    state = load();
    $("#status").textContent = `${data.options.length} options loaded · ${data.rules.maxCreationPoints} creation points per settler (from the game files) · skill levels cost 1 point`;
    bindGlobal(); syncGlobalInputs(); renderSettlers(); renderExploreControls(); renderExplore(); renderRoles();
    runAll();
  }

  // ---------- global controls ----------
  function syncGlobalInputs() {
    $("#g-template").innerHTML = `<option value="">— choose —</option>` + Object.keys(TEMPLATES).map(t => `<option ${t === state.template ? "selected" : ""}>${t}</option>`).join("");
    $("#g-count").value = state.count; $("#g-budget").value = state.budget; $("#g-cap").value = state.cap; $("#g-iw").value = state.iw;
    $("#g-bad").checked = state.allowBad; $("#g-maxbad").value = state.maxBad; $("#g-maxperks").value = state.maxPerks ?? 10;
    $("#g-pool").textContent = `= ${pool()} shared points`;
  }
  const changed = () => { state.template = ""; $("#g-template").value = ""; };
  function bindGlobal() {
    document.querySelectorAll("#tabs button").forEach(b => b.onclick = () => {
      document.querySelectorAll("#tabs button").forEach(x => x.classList.toggle("active", x === b));
      document.querySelectorAll(".tab").forEach(t => t.classList.toggle("active", t.id === "tab-" + b.dataset.tab));
      if (b.dataset.tab === "explore") { renderExploreControls(); renderExplore(); }
    });
    $("#g-template").onchange = e => { if (!e.target.value) return; const keep = { banned: state.banned }; state = { ...fromTemplate(e.target.value), ...keep }; save(); syncGlobalInputs(); renderSettlers(); runAll(); };
    $("#g-count").onchange = e => {
      state.count = Math.max(1, Math.min(6, +e.target.value || 1)); changed();
      while (state.settlers.length < state.count) state.settlers.push(newSettler(state.settlers.length));
      save(); syncGlobalInputs(); renderSettlers(); runAll();
    };
    const num = (id, key, after) => $(id).onchange = e => { state[key] = +e.target.value || 0; save(); syncGlobalInputs(); if (after) after(); runAll(); };
    num("#g-budget", "budget"); num("#g-cap", "cap", renderSettlers); num("#g-iw", "iw"); num("#g-maxbad", "maxBad"); num("#g-maxperks", "maxPerks", renderSettlers);
    $("#g-bad").onchange = e => { state.allowBad = e.target.checked; save(); runAll(); };
    $("#run").onclick = runAll;
    $("#reset").onclick = () => { if (confirm("Reset everything to the default template?")) { state = fromTemplate(DEFAULT_TEMPLATE); save(); syncGlobalInputs(); renderSettlers(); runAll(); } };
  }

  // ---------- settler cards ----------
  const optList = (cat, sel, extra = "") => `<option value="">Auto</option>${extra}` +
    data.options.filter(o => o.cat === cat).slice().sort((a, b) => a.name.localeCompare(b.name))
      .map(o => `<option value="${o.idx}" ${String(sel) === String(o.idx) ? "selected" : ""}>${esc(o.name)} (${o.cost})</option>`).join("");

  function renderSettlers() {
    const wrap = $("#settlers"); wrap.innerHTML = "";
    team().forEach((s, i) => {
      const el = document.createElement("div"); el.className = "card"; el.id = "card-" + i;
      el.innerHTML = `
        <div class="card-head">
          <input class="name" value="${esc(s.name)}" data-k="name">
          <select data-k="preset" title="Role preset">${Object.keys(PRESETS).map(p => `<option ${p === s.preset ? "selected" : ""}>${p}</option>`).join("")}</select>
          <select data-k="gender"><option ${s.gender === "Male" ? "selected" : ""}>Male</option><option ${s.gender === "Female" ? "selected" : ""}>Female</option></select>
        </div>
        <div class="skillgrid">
          <span class="gh">Skill</span><span class="gh">Priority</span><span class="gh">Target</span><span class="gh">Skill</span><span class="gh">Priority</span><span class="gh">Target</span>
          ${SKILLS.map(k => `<span class="${s.weights[k] ? "on" : ""}">${SKILL_NAME[k]}</span>
            <input type="number" min="0" max="10" step="0.5" value="${s.weights[k] || 0}" data-w="${k}">
            <input type="number" min="0" max="${state.cap}" value="${s.weights[k] ? (s.targets[k] ?? state.cap) : ""}" placeholder="–" data-t="${k}" ${s.weights[k] ? "" : "disabled"}>`).join("")}
        </div>
        <details><summary>Locks &amp; must-haves</summary>
          <div class="locks">
            <label>Old bg <select data-l="old">${optList("old_background", s.lock.old)}</select></label>
            <label>Recent bg <select data-l="recent">${optList("recent_background", s.lock.recent)}</select></label>
            <label>Pseudonym <select data-l="pseudo">${optList("pseudonym", s.lock.pseudo, `<option value="none" ${s.lock.pseudo === "none" ? "selected" : ""}>None</option>`)}</select></label>
            ${Array.from({ length: Math.min(5, state.maxPerks ?? 10) }, (_, j) => `<label>Must-have perk ${j + 1} <select data-mp="${j}"><option value="">—</option>${data.options.filter(o => o.cat === "perk").sort((a, b) => a.name.localeCompare(b.name)).map(o => `<option value="${esc(o.id)}" ${o.id === (s.mustPerks || [])[j] ? "selected" : ""}>${esc(o.name)} (${o.cost})</option>`).join("")}</select></label>`).join("")}
          </div>
        </details>
        <div class="results" id="res-${i}"><p class="meta">…</p></div>`;
      el.querySelectorAll("[data-k]").forEach(inp => inp.onchange = () => {
        const k = inp.dataset.k; s[k] = inp.value;
        if (k === "preset") { if (PRESETS[inp.value]) applyPreset(s, inp.value); changed(); save(); renderSettlers(); runAll(); return; }
        save(); if (k === "name") { renderResults(); renderSummary(); return; }
        if (k === "age") renderSettlers();
        runAll();
      });
      el.querySelectorAll("[data-w]").forEach(inp => inp.onchange = () => {
        const k = inp.dataset.w, v = +inp.value || 0;
        if (v) { s.weights[k] = v; if (s.targets[k] == null) s.targets[k] = Math.min(10, state.cap); } else { delete s.weights[k]; delete s.targets[k]; }
        s.preset = "Custom"; changed(); save(); renderSettlers(); runAll();
      });
      el.querySelectorAll("[data-t]").forEach(inp => inp.onchange = () => {
        s.targets[inp.dataset.t] = Math.max(0, Math.min(state.cap, +inp.value || 0)); s.preset = "Custom"; changed(); save(); runAll();
      });
      el.querySelectorAll("[data-l]").forEach(inp => inp.onchange = () => { s.lock[inp.dataset.l] = inp.value; save(); runAll(); });
      el.querySelectorAll("[data-mp]").forEach(inp => inp.onchange = () => {
        s.mustPerks = s.mustPerks || []; s.mustPerks[+inp.dataset.mp] = inp.value; changed(); save(); runAll();
      });
      wrap.appendChild(el);
    });
  }

  const gcfg = () => ({ pool: pool(), cap: state.cap, interestWeight: state.iw, allowBad: state.allowBad, maxBad: state.maxBad, maxPerks: state.maxPerks ?? 10, bannedPerks: new Set(state.banned) });
  const scfg = s => ({ weights: s.weights, targets: s.targets, gender: s.gender, lock: s.lock, mustPerks: [...new Set((s.mustPerks || []).filter(Boolean))] });

  let runToken = 0;
  function runAll() {
    const tok = ++runToken;
    $("#team-summary").innerHTML = `<p class="meta">Optimizing ${team().length} settler(s) over a ${pool()}-point pool…</p>`;
    team().forEach((_, i) => { const b = $("#res-" + i); if (b) b.innerHTML = `<p class="meta">Optimizing…</p>`; });
    setTimeout(() => {
      if (tok !== runToken) return;
      const t0 = performance.now();
      const r = C.optimizeTeam(data, team().map(scfg), gcfg());
      r.ms = Math.round(performance.now() - t0);
      if (!r.error) r.sel = team().map(() => 0);
      res = r; renderResults(); renderSummary();
    }, 20);
  }

  // swap one settler's traits for an alternative, then re-spend skill points for the whole team
  function chooseAlt(i, j) {
    res.sel[i] = j;
    const traits = res.settlers.map((x, k) => (k === i ? res.alts[i][j] : x));
    const a = C.allocate(data, traits, team().map(scfg), gcfg());
    if (a.error) { alert(a.error); return; }
    Object.assign(res, { settlers: a.settlers, value: a.value, unspent: a.unspent });
    renderResults(); renderSummary();
  }

  // text helpers
  function personalise(text, s) {
    return esc(text).replace(/&lt;name&gt;/g, esc(s.name))
      .replace(/\[([^\/\]]*)\/([^\]]*)\]/g, (_, m, f) => (s.gender === "Female" ? f : m));
  }
  const titleOf = b => `${b.recent.name} ${b.old.name.toLowerCase()}${b.pseudo ? ` “${b.pseudo.name}”` : ""}`;
  const sameTraits = (a, b) => a.old === b.old && a.recent === b.recent && a.pseudo === b.pseudo && a.perks.length === b.perks.length && a.perks.every(p => b.perks.includes(p));
  const lvlChip = (skill, lvl) => `<span class="chip lvl-${lvl}" title="${LVL_NAME[lvl]}">${LVL_ICON[lvl] || ""} ${SKILL_NAME[skill]}: ${LVL_NAME[lvl]}</span>`;
  function otherInterests(b) {
    const out = [];
    for (const p of [b.old, b.recent, b.pseudo, ...b.perks].filter(Boolean))
      for (const it of p.ints) { const I = data.interests[it.id]; if (I && !I.skill) out.push(`${I.name}: ${LVL_NAME[it.level] || it.level}`); }
    return [...new Set(out)];
  }

  function renderResults() {
    team().forEach((s, i) => {
      const box = $("#res-" + i); if (!box) return;
      if (!res) return;
      if (res.error) { box.innerHTML = `<p class="over">${esc(res.error)}</p>`; return; }
      const b = res.settlers[i];
      const rows = SKILLS.filter(k => s.weights[k] || b.final[k]);
      const scale = Math.max(state.cap, ...rows.map(k => b.final[k]));
      const parts = [["old_background", b.old], ["recent_background", b.recent], ["pseudonym", b.pseudo], ...b.perks.map(p => ["perk", p])].filter(x => x[1]);
      box.innerHTML = `
        <div class="build selected">
          <div class="build-title"><b>${esc(titleOf(b))}</b><span class="score" title="Value of this settler">${b.value.toFixed(1)}</span></div>
          <div class="meta">${b.traitCost + b.negCost + b.skillCost} points: traits ${b.traitCost} + skill levels ${b.skillCost}${b.negCost ? ` + <span class="over" title="Negative skill modifiers still cost points: the level can't go below 0">${b.negCost} for negative modifiers</span>` : ""}</div>
          <div class="chips">${parts.map(([c, o]) => `<span class="chip cat-${c} ${c === "perk" && o.cost < 0 ? "badperk" : ""}" title="${esc(CAT_LABEL[c])}: ${personalise(o.desc, s)}">${esc(o.name)} <small>(${o.cost})</small></span>`).join("")}</div>
          <div class="chips">${Object.entries(b.lvl).filter(([, l]) => l !== 3).sort((x, y) => y[1] - x[1]).map(([k, l]) => lvlChip(k, l)).join("")}
            ${otherInterests(b).map(t => `<span class="chip">${esc(t)}</span>`).join("")}</div>
          <div class="bars">${rows.map(k => {
            const fin = Math.max(0, b.final[k]), tr = Math.max(0, Math.min(b.fromTraits[k], fin)), bo = fin - tr, tgt = s.weights[k] ? s.targets[k] ?? state.cap : null;
            return `<span>${SKILL_NAME[k]}${s.weights[k] ? ` <small class="meta">p${s.weights[k]}</small>` : ""}</span>
              <div class="bar" title="${b.fromTraits[k]} from traits${b.bought[k] ? ` + ${b.bought[k]} bought` : ""}${tgt != null ? ` · target ${tgt}` : ""}">
                <i class="tr" style="width:${tr / scale * 100}%"></i><i class="bo" style="width:${bo / scale * 100}%"></i>
                ${tgt != null ? `<em style="left:${tgt / scale * 100}%"></em>` : ""}</div>
              <span><b>${fin}</b>${b.fromTraits[k] < 0 ? ` <small class="neg">(${b.fromTraits[k]})</small>` : ""}</span>`;
          }).join("")}</div>
          ${b.perks.length ? `<details><summary>Perk details</summary>${b.perks.map(p => `<p class="desc"><b>${esc(p.name)}</b> (${p.cost}): ${personalise(p.desc, s)}${p.attrs.length ? `<br><i>${esc(p.attrs.map(a => `${a.key} ×${a.value}`).join(", "))}</i>` : ""}</p>`).join("")}</details>` : ""}
          <details><summary>Background story</summary><p class="desc">${personalise(b.old.desc, s)}</p><p class="desc">${personalise(b.recent.desc, s)}</p>${b.pseudo ? `<p class="desc">${personalise(b.pseudo.desc, s)}</p>` : ""}</details>
        </div>
        <div class="alts"><div class="meta">Alternative trait sets (skill points are re-spent when you pick one):</div>
          ${(res.alts[i] || []).map((x, j) => sameTraits(x, b) ? "" : `<button data-alt="${j}">${esc(titleOf(x))} · ${x.perks.map(p => esc(p.name)).join(", ") || "no perks"} · traits ${x.cost}</button>`).join("")}</div>`;
      box.querySelectorAll("[data-alt]").forEach(btn => btn.onclick = () => chooseAlt(i, +btn.dataset.alt));
    });
  }

  // ---------- team overview ----------
  function renderSummary() {
    if (!res) return;
    if (res.error) { $("#team-summary").innerHTML = `<p class="over">${esc(res.error)}</p>`; return; }
    const T = team(), B = res.settlers;
    const used = res.pool - res.unspent;
    let html = `<div class="poolbar"><div style="width:${Math.min(100, used / res.pool * 100)}%"></div></div>
      <p><b>${used} / ${res.pool}</b> points used · team value <b class="score">${res.value.toFixed(1)}</b> · ${res.ms ?? "?"} ms
      ${res.unspent > 0 ? `<br><span class="over">${res.unspent} points unspent:</span> every target and the max level are reached. Add more skills (or raise targets) so the optimizer has somewhere to spend them.` : ""}</p>
      <div class="table-wrap"><table><tr><th>Skill</th>${T.map(s => `<th class="num">${esc(s.name)}</th>`).join("")}<th class="num">Best</th><th>Coverage</th></tr>`;
    const wanted = new Set(T.flatMap(s => Object.keys(s.weights)));
    for (const k of SKILLS) {
      const vals = B.map(b => Math.max(0, b.final[k])), best = Math.max(...vals), lv = B.map(b => b.lvl[k]);
      const covered = best >= 10 || lv.some(l => l >= 4 && l !== 6);
      html += `<tr><td>${SKILL_NAME[k]}${wanted.has(k) ? " ★" : ""}</td>${vals.map((v, j) => `<td class="num ${v > 0 ? "pos" : "zero"} ${v === best && best > 0 ? "best" : ""}">${v || "·"}${B[j].bought[k] ? ` <small class="meta">(+${B[j].bought[k]})</small>` : ""} ${lv[j] && lv[j] !== 3 ? `<span title="${LVL_NAME[lv[j]]}">${LVL_ICON[lv[j]]}</span>` : ""}</td>`).join("")}
        <td class="num">${best}</td><td class="${covered ? "" : wanted.has(k) ? "over" : "meta"}">${covered ? "✓ covered" : wanted.has(k) ? "wanted, but weak" : "—"}</td></tr>`;
    }
    html += `<tr><td><b>Points</b></td>${B.map(b => `<td class="num">${b.traitCost + b.negCost + b.skillCost}<br><small class="meta">${b.traitCost}+${b.skillCost}${b.negCost ? "+" + b.negCost : ""}</small></td>`).join("")}<td class="num"><b>${used}</b></td><td></td></tr></table></div>
      <p class="hint">Numbers are final skill levels; (+n) = levels bought with points. ★ = skill some settler has a priority for · ♥♥ Passionate · ♥ Eager · ✗ Unwilling · ✗✗ Resentful · ⊘ Incapable · "covered" = someone has 10+ or is Eager/Passionate.</p>`;
    $("#team-summary").innerHTML = html;
  }

  // ---------- explore ----------
  let sortKey = "score", sortDir = -1;
  function renderExploreControls() {
    const sel = $("#x-settler"), cur = sel.value;
    sel.innerHTML = `<option value="">(no weights)</option>` + team().map((s, i) => `<option value="${i}">${esc(s.name)}</option>`).join("");
    sel.value = cur !== "" && cur < state.count ? cur : "0";
    for (const id of ["#x-skill", "#x-int"]) { const el = $(id), v = el.value; el.innerHTML = `<option value="">—</option>` + SKILLS.map(k => `<option value="${k}">${SKILL_NAME[k]}</option>`).join(""); el.value = v; }
    ["#x-cat", "#x-q", "#x-settler", "#x-skill", "#x-min", "#x-int", "#x-intlvl"].forEach(id => $(id).oninput = renderExplore);
  }
  function renderExplore() {
    const cat = $("#x-cat").value, q = $("#x-q").value.toLowerCase(), si = $("#x-settler").value;
    const w = si !== "" && state.settlers[+si] ? state.settlers[+si].weights : {};
    const fs = $("#x-skill").value, fmin = +$("#x-min").value, fi = $("#x-int").value, fil = +$("#x-intlvl").value;
    const banned = new Set(state.banned);
    let rows = data.options.filter(o => (!cat || o.cat === cat) && (!q || (o.name + " " + o.id + " " + o.desc).toLowerCase().includes(q))
      && (!fs || o.sk[fs] >= fmin) && (!fi || (o.lvl[fi] >= fil && o.lvl[fi] !== 6)))
      .map(o => ({ o, score: C.approxScore(o, w, state.iw) }));
    const get = r => sortKey === "score" ? r.score : sortKey === "name" ? r.o.name : sortKey === "cat" ? r.o.cat : sortKey === "cost" ? r.o.cost : r.o.sk[sortKey];
    rows.sort((a, b) => { const x = get(a), y = get(b); return (x < y ? -1 : x > y ? 1 : 0) * sortDir; });
    const th = (k, label, cls = "") => `<th data-sort="${k}" class="${cls}">${label}${sortKey === k ? (sortDir < 0 ? " ▼" : " ▲") : ""}</th>`;
    let html = `<tr>${th("cat", "Category")}${th("name", "Name")}${th("cost", "Cost", "num")}${th("score", "Score", "num")}${SKILLS.map(k => th(k, SKILL_NAME[k].replace(" ", "<br>"), "num")).join("")}<th>Interests</th><th>Notes</th><th></th></tr>`;
    for (const { o, score } of rows) {
      const ints = o.ints.map(it => { const I = data.interests[it.id]; return `<span class="chip lvl-${it.level}">${esc(I ? I.name : it.id)}: ${LVL_NAME[it.level] || it.level}</span>`; }).join(" ");
      const notes = [o.attrs.length ? o.attrs.map(a => `${a.key} ×${a.value}`).join(", ") : "", o.conflicts.length ? "Conflicts: " + o.conflicts.join(", ") : "", o.onlyFor.length ? (o.cat === "perk" ? "Random-gen only for: " : "Only for: ") + o.onlyFor.join(", ") : ""].filter(Boolean).join(" · ");
      html += `<tr title="${esc(o.desc)}"><td>${CAT_LABEL[o.cat]}</td><td><b>${esc(o.name)}</b></td><td class="num ${o.cost < 0 ? "neg" : ""}">${o.cost}</td><td class="num"><b>${score ? score.toFixed(1) : ""}</b></td>
        ${SKILLS.map(k => `<td class="num ${o.sk[k] > 0 ? "pos" : o.sk[k] < 0 ? "neg" : "zero"}">${o.sk[k] || "·"}</td>`).join("")}
        <td>${ints}</td><td class="meta">${esc(notes)}</td>
        <td>${o.cat === "perk" ? `<button class="small ${banned.has(o.id) ? "on" : ""}" data-ban="${esc(o.id)}">${banned.has(o.id) ? "banned" : "ban"}</button>` : ""}</td></tr>`;
    }
    const t = $("#x-table"); t.innerHTML = html;
    t.querySelectorAll("[data-sort]").forEach(h => h.onclick = () => { const k = h.dataset.sort; sortDir = sortKey === k ? -sortDir : (k === "name" || k === "cat" ? 1 : -1); sortKey = k; renderExplore(); });
    t.querySelectorAll("[data-ban]").forEach(b => b.onclick = () => {
      const id = b.dataset.ban, set = new Set(state.banned); set.has(id) ? set.delete(id) : set.add(id);
      state.banned = [...set]; save(); renderExplore(); runAll();
    });
  }

  // ---------- roles ----------
  function renderRoles() {
    const nice = s => s.split(";").filter(Boolean).map(x => { const [k, v] = x.split(">="); return `${SKILL_NAME[k] || k} ≥ ${v}`; }).join(", ") || "none";
    const ints = s => s.split(";").filter(Boolean).map(x => { const [k, v] = x.split("="); return `${data.interests[k]?.name || k}: ${LVL_NAME[v] || v}`; }).join(", ");
    $("#r-table").innerHTML = `<tr><th>Role</th><th class="num">Level</th><th>Skill requirements</th><th class="num">Hours</th><th>Room</th><th>Interest changes</th><th>Description</th></tr>` +
      data.roles.map(r => `<tr><td><b>${esc(r.name)}</b></td><td class="num">${r.level}</td><td>${esc(nice(r.skill_requirements))}</td><td class="num">${r.hours_requirement}</td>
        <td>${esc(r.room_requirement.replace(/_/g, " "))}</td><td>${esc(ints(r.interests))}</td><td class="meta">${r.level === "1" ? esc(r.description) : ""}</td></tr>`).join("");
  }

  boot();
})();
