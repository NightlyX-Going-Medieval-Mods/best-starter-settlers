// core.js — data parsing, scoring and the team optimizer. No DOM code here,
// so it can be tested with Node:  node -e "const c=require('./core.js')"
//
// Rules below were checked against the decompiled game code (Assembly-CSharp):
//  - Creation points per settler = trait costs + Σ(skill level − level from traits)
//    (HumanoidUtils.UpdateCreationPoints / GetSkillCreationPoints). Team max = 90 × settlers.
//  - Skill levels are clamped to 0..50 (WorkerSkill). A negative trait modifier still
//    counts, so a −3 skill sitting at 0 costs 3 points.
//  - Interests come only from traits. Same level twice = unchanged, otherwise
//    clamp(current + added − 3, 1, 5) (GoalPreferenceLevelRepository.GetCumulativeLevelData).
//    Order: perks, recent background, old background, pseudonym; then per skill.
//  - Advanced Customization allows up to 10 perks at any age (CharacterStatsEdit.maxPerks).
//  - Background/pseudonym "only for" body types are enforced; perks are not filtered.
(function (root) {
  "use strict";

  const SKILLS = ["AnimalHandling", "Art", "Botanical", "Carpentry", "Construction", "Culinary",
    "Intelectual", "Marksman", "Medicine", "Melee", "Mining", "Smithing", "Speechcraft", "Tailoring"];
  const SKILL_NAME = {
    AnimalHandling: "Animal Handling", Art: "Art", Botanical: "Botany", Carpentry: "Carpentry",
    Construction: "Construction", Culinary: "Culinary", Intelectual: "Intellectual", Marksman: "Marksman",
    Medicine: "Medicine", Melee: "Melee", Mining: "Mining", Smithing: "Smithing",
    Speechcraft: "Speechcraft", Tailoring: "Tailoring",
  };
  // Interest level code -> score bonus (per unit of priority). Codes from interest_levels.csv.
  const LEVEL_BONUS = { 1: -2, 2: -1, 3: 0, 4: 1, 5: 2, 6: -3 };
  const NSK = SKILLS.length, NONE = 0, EXTRA = 0.2;

  // ---------- CSV ----------
  function parseCSV(text) {
    const rows = []; let row = [], field = "", q = false;
    for (let i = 0; i < text.length; i++) {
      const c = text[i];
      if (q) {
        if (c === '"') { if (text[i + 1] === '"') { field += '"'; i++; } else q = false; }
        else field += c;
      } else if (c === '"') q = true;
      else if (c === ",") { row.push(field); field = ""; }
      else if (c === "\n" || c === "\r") {
        if (c === "\r" && text[i + 1] === "\n") i++;
        row.push(field); rows.push(row); row = []; field = "";
      } else field += c;
    }
    if (field !== "" || row.length) { row.push(field); rows.push(row); }
    const head = rows.shift() || [];
    return rows.filter(r => r.length > 1 || r[0] !== "").map(r => Object.fromEntries(head.map((h, i) => [h, r[i] ?? ""])));
  }
  const splitList = s => (s ? s.split(";").filter(Boolean) : []);
  const kv = s => splitList(s).map(p => { const i = p.indexOf("="); return [p.slice(0, i), p.slice(i + 1)]; });

  // The game's rule for stacking two interest levels (GetCumulativeLevelData).
  function combineLevel(a, b) {
    const hasA = a != null && a !== NONE, hasB = b != null && b !== NONE;
    if (!hasB) return a; if (!hasA) return b >= 1 ? b : a;
    if (a === b) return a;
    return Math.min(5, Math.max(1, a + b - 3));
  }

  // ---------- dataset ----------
  function buildData(csvs) {
    const interests = {};
    for (const r of parseCSV(csvs.interests)) interests[r.interest_id] = { id: r.interest_id, name: r.name, skill: r.related_skill || null, desc: r.description };
    const levels = {};
    for (const r of parseCSV(csvs.interest_levels)) levels[+r.level] = { level: +r.level, name: r.name, mult: +r.skill_multiplier, desc: r.description };
    const options = parseCSV(csvs.all_options).map((r, idx) => {
      const sk = {}; for (const s of SKILLS) sk[s] = +r["skill_" + s] || 0;
      const ints = kv(r.interests).map(([k, v]) => ({ id: k, level: +v }));
      const lvl = interestsToSkills(ints, interests);
      return {
        idx, cat: r.category, id: r.id, name: r.name, cost: +r.cost, sk, ints, lvl,
        attrs: kv(r.attribute_modifiers).map(([k, v]) => ({ key: k, value: +v })),
        conflicts: splitList(r.conflicts_with), onlyFor: splitList(r.only_for),
        religion: r.religious_alignment === "" ? null : +r.religious_alignment, desc: r.description,
      };
    });
    const perkById = Object.fromEntries(options.filter(o => o.cat === "perk").map(o => [o.id, o]));
    for (const p of Object.values(perkById)) for (const c of p.conflicts) if (perkById[c] && !perkById[c].conflicts.includes(p.id)) perkById[c].conflicts.push(p.id);
    const rules = {}; for (const r of parseCSV(csvs.generation_rules)) rules[r.rule] = r.value;
    return { options, interests, levels, rules, roles: parseCSV(csvs.roles), perkById };
  }

  // interests list [{id, level}] in game order -> {skill: level}
  function interestsToSkills(ints, interests) {
    const byPref = new Map();
    for (const it of ints) byPref.set(it.id, combineLevel(byPref.get(it.id), it.level));
    const out = {};
    for (const [id, l] of byPref) { const s = interests[id]?.skill; if (s && l != null && l !== NONE) out[s] = combineLevel(out[s], l); }
    return out;
  }
  // exact interests for a full settler (game order: perks, recent, old, pseudonym)
  function settlerInterests(T, interests) {
    const parts = [...T.perks, T.recent, T.old, T.pseudo].filter(Boolean);
    return interestsToSkills(parts.flatMap(p => p.ints), interests);
  }

  // Backgrounds/pseudonyms marked Male/Female are only for that body type. Perks aren't filtered in the editor.
  function allowed(o, gender) {
    if (o.cat === "perk" || !gender) return true;
    const g = o.onlyFor.filter(x => x === "Male" || x === "Female");
    return !g.length || g.includes(gender);
  }

  // quick per-option score used by the Explore table (skill mods × priority + interest bonus)
  function approxScore(o, w, iw) {
    let s = 0;
    for (const k of SKILLS) if (w[k]) { s += w[k] * o.sk[k]; if (o.lvl[k] != null) s += iw * w[k] * LEVEL_BONUS[o.lvl[k]]; }
    return s;
  }

  // =====================================================================
  // Team optimizer
  // Per settler & skill: priority w and target t.
  //   value(L) = w·min(L,t) + EXTRA·w·(min(L,cap) − t if above t) + iw·w·bonus(interest)
  // For a "price per point" λ each settler picks traits maximising value − λ·points; the shared
  // pool's leftover is then spent on skill levels across the team. Several λ are tried.
  // =====================================================================
  function toVec(parts) {
    const sk = new Float64Array(NSK), lv = new Int8Array(NSK); let cost = 0;
    for (const p of parts) {
      cost += p.cost;
      for (let i = 0; i < NSK; i++) { sk[i] += p.sk[SKILLS[i]]; const l = p.lvl[SKILLS[i]]; if (l != null) lv[i] = combineLevel(lv[i], l) || 0; }
    }
    return { sk, lv, cost };
  }
  const negCost = sk => { let n = 0; for (let i = 0; i < NSK; i++) if (sk[i] < 0) n -= sk[i]; return n; };

  function profile(s, cap) {
    const idx = [], w = [], t = [];
    SKILLS.forEach((k, i) => { const wi = +s.weights[k] || 0; if (wi > 0) { idx.push(i); w.push(wi); t.push(Math.min(cap, Math.max(0, +(s.targets?.[k] ?? cap)))); } });
    return { idx, w, t };
  }

  function valueOf(levels, lv, P, iw, cap) {
    let v = 0;
    for (let j = 0; j < P.idx.length; j++) {
      const i = P.idx[j], L = Math.min(Math.max(levels[i], 0), cap), w = P.w[j], t = P.t[j];
      v += w * Math.min(L, t) + (L > t ? EXTRA * w * (L - t) : 0);
      if (lv[i] && lv[i] !== NONE) v += iw * w * LEVEL_BONUS[lv[i]];
    }
    return v;
  }

  // best value − λ·points for a trait vector, buying levels at price λ
  function netAt(sk, lv, cost, P, iw, cap, lam) {
    let v = 0, buy = 0;
    for (let j = 0; j < P.idx.length; j++) {
      const i = P.idx[j], m = Math.min(Math.max(sk[i], 0), cap), w = P.w[j], t = P.t[j];
      v += w * Math.min(m, t) + (m > t ? EXTRA * w * (m - t) : 0);
      if (m < t && w > lam) { v += w * (t - m); buy += t - m; }
      const from = Math.max(m, t);
      if (EXTRA * w > lam && from < cap) { v += EXTRA * w * (cap - from); buy += cap - from; }
      if (lv[i] !== NONE) v += iw * w * LEVEL_BONUS[lv[i]];
    }
    return v - lam * (cost + negCost(sk) + buy);
  }

  function candidates(data, s, g) {
    const by = c => data.options.filter(o => o.cat === c && allowed(o, s.gender));
    const lock = (list, id) => (id != null && id !== "" && id !== "none" ? list.filter(o => o.idx === +id) : list);
    const banned = g.bannedPerks || new Set();
    const must = [...new Set(s.mustPerks || [])].map(id => data.perkById[id]).filter(Boolean);
    return {
      olds: lock(by("old_background"), s.lock?.old), recents: lock(by("recent_background"), s.lock?.recent),
      pseudos: s.lock?.pseudo === "none" ? [] : lock(by("pseudonym"), s.lock?.pseudo), pseudoOptional: !s.lock?.pseudo,
      perks: by("perk").filter(p => !banned.has(p.id) && !must.includes(p) && (g.allowBad || p.cost >= 0)), must,
    };
  }
  const conflicts = (p, chosen) => chosen.some(c => c.conflicts.includes(p.id) || p.conflicts.includes(c.id));
  const keyOf = b => [b.old.idx, b.recent.idx, b.pseudo?.idx ?? -1, ...b.perks.map(p => p.idx).sort((a, c) => a - c)].join(",");

  function bestTraits(data, s, g, lam, keep) {
    const P = profile(s, g.cap), iw = g.interestWeight, cap = g.cap, maxP = g.maxPerks ?? 10, maxBad = g.maxBad ?? 99;
    const C = candidates(data, s, g);
    if (C.must.length > maxP) return { error: `More must-have perks than the ${maxP}-perk limit.` };
    if (C.must.some((p, i) => conflicts(p, C.must.slice(0, i)))) return { error: "Must-have perks conflict with each other." };
    const e = toVec([]), base0 = netAt(e.sk, e.lv, 0, P, iw, cap, lam);
    const h = o => { const v = toVec([o]); return netAt(v.sk, v.lv, v.cost, P, iw, cap, lam) - base0; };
    const top = (list, k) => list.map(o => [o, h(o)]).sort((a, b) => b[1] - a[1]).slice(0, k).map(x => x[0]);
    const olds = top(C.olds, 10), recents = top(C.recents, 10);
    const pseudos = [...(C.pseudoOptional ? [null] : []), ...top(C.pseudos, 7)];
    if (!olds.length || !recents.length || !pseudos.length) return { error: "No background/pseudonym fits these locks and body type." };
    const ranked = C.perks.map(p => [p, h(p)]).sort((a, b) => b[1] - a[1]);
    const badOf = list => list.filter(p => p.cost < 0).length;
    const canAdd = (p, ch) => ch.length < maxP && !ch.includes(p) && !conflicts(p, ch) && (p.cost >= 0 || badOf(ch) < maxBad);

    // perk sets to try: all small sets (≤3) of the top-ranked perks + greedy chains of any length
    const sets = [], seen = new Set();
    const addSet = ch => { const k = ch.map(p => p.idx).sort((a, b) => a - b).join(","); if (!seen.has(k)) { seen.add(k); sets.push(ch.slice()); } };
    const cands = ranked.slice(0, 12).map(x => x[0]);
    (function rec(st, ch) {
      addSet(ch); if (ch.length - C.must.length >= 3) return;
      for (let i = st; i < cands.length; i++) if (canAdd(cands[i], ch)) { ch.push(cands[i]); rec(i + 1, ch); ch.pop(); }
    })(0, C.must.slice());
    for (let start = 0; start < Math.min(6, ranked.length); start++) {
      const ch = C.must.slice();
      for (const [p, v] of [ranked[start], ...ranked]) if (v > 0 && canAdd(p, ch)) { ch.push(p); addSet(ch); }
    }
    const pv = sets.map(st => ({ s: st, v: toVec(st) }));

    // exhaustive over the pruned backgrounds/pseudonyms × perk sets
    let best = [];
    const sk = new Float64Array(NSK), lv = new Int8Array(NSK);
    for (const O of olds) for (const R of recents) for (const Ps of pseudos) {
      const b = toVec(Ps ? [O, R, Ps] : [O, R]);
      for (const x of pv) {
        for (let i = 0; i < NSK; i++) { sk[i] = b.sk[i] + x.v.sk[i]; lv[i] = combineLevel(b.lv[i], x.v.lv[i]) || 0; }
        const cost = b.cost + x.v.cost;
        if (cost + negCost(sk) > g.pool) continue;
        best.push({ old: O, recent: R, pseudo: Ps, perks: x.s, net: netAt(sk, lv, cost, P, iw, cap, lam) });
        if (best.length > keep * 40) { best.sort((a, c) => c.net - a.net); best.length = keep * 4; }
      }
    }
    best.sort((a, c) => c.net - a.net);
    const uniq = [], ks = new Set();
    for (const b of best) { const k = keyOf(b); if (!ks.has(k)) { ks.add(k); uniq.push(b); } if (uniq.length >= keep * 2) break; }

    // local search on perks: add / drop / swap until nothing improves (exact interest stacking)
    const exactNet = b => {
      const v = toVec([b.old, b.recent, b.pseudo, ...b.perks].filter(Boolean));
      const L = settlerInterests(b, data.interests);
      const lv2 = new Int8Array(NSK); SKILLS.forEach((k, i) => { lv2[i] = L[k] || 0; });
      if (v.cost + negCost(v.sk) > g.pool) return -Infinity;
      return netAt(v.sk, lv2, v.cost, P, iw, cap, lam);
    };
    const pool = C.perks;
    for (const b of uniq) {
      b.net = exactNet(b);
      for (let it = 0; it < 25; it++) {
        let bestMove = null, bestNet = b.net + 1e-9;
        const free = b.perks.filter(p => !C.must.includes(p));
        const tryP = perks => { const nb = { ...b, perks }; const n = exactNet(nb); if (n > bestNet) { bestNet = n; bestMove = perks; } };
        for (const p of pool) if (canAdd(p, b.perks)) tryP([...b.perks, p]);
        for (const q of free) {
          const rest = b.perks.filter(x => x !== q); tryP(rest);
          for (const p of pool) if (p !== q && canAdd(p, rest)) tryP([...rest, p]);
        }
        if (!bestMove) break;
        b.perks = bestMove; b.net = bestNet;
      }
    }
    uniq.sort((a, c) => c.net - a.net || badOf(a.perks) - badOf(c.perks));
    const out = [], k2 = new Set();
    for (const b of uniq) { const k = keyOf(b); if (!k2.has(k)) { k2.add(k); out.push(b); } if (out.length >= keep) break; }
    // trait cost for display (perks may have changed during the local search)
    for (const b of out) b.cost = [b.old, b.recent, b.pseudo, ...b.perks].filter(Boolean).reduce((a, p) => a + p.cost, 0);
    return { builds: out };
  }

  // Spend the shared pool on skill levels for fixed trait choices.
  function allocate(data, team, settlers, g) {
    const cap = g.cap, iw = g.interestWeight;
    const st = team.map((T, k) => {
      const v = toVec([T.old, T.recent, T.pseudo, ...T.perks].filter(Boolean));
      const L = settlerInterests(T, data.interests), lv = new Int8Array(NSK);
      SKILLS.forEach((s, i) => { lv[i] = L[s] || 0; });
      const lvlFloor = Float64Array.from(v.sk, x => Math.min(Math.max(x, 0), cap));
      return { T, P: profile(settlers[k], cap), mods: v.sk, lv, L, cost: v.cost, neg: negCost(v.sk), levels: lvlFloor, bought: new Array(NSK).fill(0) };
    });
    const fixed = st.reduce((a, x) => a + x.cost + x.neg, 0);
    let left = g.pool - fixed;
    if (left < 0) return { error: `Trait choices cost ${fixed}, over the ${g.pool}-point pool.` };
    const segs = [];
    st.forEach((x, k) => x.P.idx.forEach((i, j) => {
      const m = x.levels[i], w = x.P.w[j], t = x.P.t[j];
      if (m < t) segs.push({ k, i, n: t - m, ratio: w, stage: 0 });
      const from = Math.max(m, t); if (from < cap) segs.push({ k, i, n: cap - from, ratio: EXTRA * w, stage: 1 });
    }));
    segs.sort((a, b) => b.ratio - a.ratio || a.stage - b.stage);
    for (const sg of segs) {
      if (left <= 0) break;
      const n = Math.min(sg.n, left); st[sg.k].levels[sg.i] += n; st[sg.k].bought[sg.i] += n; left -= n;
    }
    const settlersOut = st.map(x => ({
      ...x.T, traitCost: x.cost, negCost: x.neg, skillCost: x.bought.reduce((a, b) => a + b, 0),
      final: Object.fromEntries(SKILLS.map((k, i) => [k, x.levels[i]])),
      fromTraits: Object.fromEntries(SKILLS.map((k, i) => [k, x.mods[i]])),
      bought: Object.fromEntries(SKILLS.map((k, i) => [k, x.bought[i]])),
      lvl: x.L, value: valueOf(x.levels, x.lv, x.P, iw, cap),
    }));
    return { settlers: settlersOut, value: settlersOut.reduce((a, x) => a + x.value, 0), unspent: left, pool: g.pool };
  }

  // settlers: [{weights, targets, gender, lock:{old,recent,pseudo}, mustPerks:[ids]}]
  // g: { pool, cap, maxPerks, interestWeight, allowBad, maxBad, bannedPerks:Set }
  function optimizeTeam(data, settlers, g) {
    const ws = [...new Set(settlers.flatMap(s => Object.values(s.weights).map(Number).filter(x => x > 0)))];
    if (!ws.length) return { error: "Give at least one skill a priority." };
    const lams = [...new Set([0, ...ws, ...ws.map(w => w * EXTRA), ...ws.map(w => w * 0.6)].map(x => +x.toFixed(3)))].sort((a, b) => a - b);
    let bestTeam = null;
    for (const lam of lams) {
      const per = settlers.map(s => bestTraits(data, s, g, lam, 1));
      const err = per.find(p => p.error); if (err) return { error: err.error };
      if (per.some(p => !p.builds.length)) continue;
      const team = allocate(data, per.map(p => p.builds[0]), settlers, g);
      if (team.error) continue;
      if (!bestTeam || team.value > bestTeam.value + 1e-9) bestTeam = { ...team, lam };
    }
    if (!bestTeam) return { error: "Even the cheapest trait choices don't fit the pool." };
    const alts = settlers.map(s => bestTraits(data, s, g, bestTeam.lam, 6).builds);
    return { ...bestTeam, alts, lams };
  }

  const api = { SKILLS, SKILL_NAME, LEVEL_BONUS, EXTRA, parseCSV, buildData, allowed, approxScore, combineLevel, settlerInterests, optimizeTeam, allocate, bestTraits };
  if (typeof module !== "undefined" && module.exports) module.exports = api; else root.GMCore = api;
})(this);
