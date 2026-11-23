#!/usr/bin/env python3
"""Extract Going Medieval settler-creation data into data/*.csv (read-only on the game).

Usage (from the going-medieval folder):
    python3 tools/extract_data.py "/mnt/c/Program Files (x86)/Steam/steamapps/common/Going Medieval/Going Medieval_Data" data

Sources:
  - <Data>/StreamingAssets/Worker/*.json, Roles/Role.json   game data (plain JSON)
  - <Data>/resources.assets                                   English text: a Unity table where each
    entry is <len><key><pad> <int32> <int32 count> then <count> strings, English first.
Only the Python standard library is used.
"""
import csv, json, os, re, struct, sys

if len(sys.argv) != 3:
    sys.exit(__doc__)
GD, OUT = sys.argv[1], sys.argv[2]
SA = os.path.join(GD, "StreamingAssets")
os.makedirs(OUT, exist_ok=True)

def load(f):
    rows = json.load(open(os.path.join(SA, f), encoding="utf-8-sig"))["repository"]
    return [r for r in rows if not r.get("hideInGame")]

D = {"perks": load("Worker/Perk.json"), "bg_old": load("Worker/Background.json"),
     "bg_recent": load("Worker/BackStory.json"), "pseudonyms": load("Worker/Pseudonym.json"),
     "interests": load("Worker/GoalPreference.json"), "roles": load("Roles/Role.json"),
     "interest_levels": load("Worker/GoalPreferenceLevelData.json")}
G = json.load(open(os.path.join(SA, "Worker/GenerationRules.json"), encoding="utf-8-sig"))["repository"][0]

# ---------- English localization from resources.assets ----------
blob = open(os.path.join(GD, "resources.assets"), "rb").read()

def read_str(p):
    n = struct.unpack_from("<I", blob, p)[0]
    return blob[p + 4:p + 4 + n].decode("utf-8"), (p + 4 + n + 3) & ~3

_cache = {}
def english(key):
    if not key:
        return None
    if key in _cache:
        return _cache[key]
    kb = key.encode(); pat = struct.pack("<I", len(kb)) + kb; out = None; start = 0
    while out is None:
        i = blob.find(pat, start)
        if i < 0:
            break
        start = i + 1
        try:
            _, p = read_str(i)
            _, count = struct.unpack_from("<II", blob, p)
            if 1 <= count <= 40:
                out, _ = read_str(p + 8)
        except Exception:
            pass
    _cache[key] = out
    return out

# ---------- helpers ----------
SK = ["AnimalHandling", "Art", "Botanical", "Carpentry", "Construction", "Culinary", "Intelectual",
      "Marksman", "Medicine", "Melee", "Mining", "Smithing", "Speechcraft", "Tailoring"]
ENUM = {1: "AnimalHandling", 2: "Botanical", 3: "Carpentry", 4: "Construction", 5: "Culinary", 6: "Intelectual",
        7: "Marksman", 8: "Medicine", 9: "Melee", 10: "Smithing", 11: "Speechcraft", 12: "Tailoring", 13: "Mining", 14: "Art"}
# WorkerCharacteristicType. In option data, ignoreCharacteristicType means "ONLY FOR this type"
# (checked in the decompiled code: HumanoidUtils.GetPhysicalIgnoreTypes + *Repository.GetAvailable*).
CHT = {1: "Male", 2: "Female", 3: "Underweight", 4: "OptimalWeight", 5: "Overweight", 6: "Short", 7: "NormalHeight", 8: "Tall"}
LV = {r["goalPreferenceLevel"]: r for r in D["interest_levels"]}
pretty = lambda i: re.sub(r'(?<=[a-z])(?=[A-Z])', ' ', i).replace(" Preference", "").strip()
lk = lambda r: r["locKeys"][0]
def tx(key, fallback):
    v = english(key)
    return v if v else fallback

def write(name, header, rows):
    with open(os.path.join(OUT, name), "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f); w.writerow(header); w.writerows(rows)
    print(f"{name}: {len(rows)} rows")

# ---------- CSVs ----------
write("skills.csv", ["skill_id", "skill", "enum_number"],
      [[s, pretty(s), next((k for k, v in ENUM.items() if v == s), "")] for s in SK])
write("interest_levels.csv", ["level", "name", "skill_multiplier", "description"],
      [[k, v["id"], v.get("relatedSkillMultiplier"), tx(lk(v).get("info"), "")] for k, v in sorted(LV.items())])
write("interests.csv", ["interest_id", "name", "related_skill", "description", "related_goals"],
      [[r["id"], tx(lk(r).get("name"), pretty(r["id"])), ENUM.get(r.get("relatedSkill"), ""), tx(lk(r).get("info"), ""),
        ";".join(r.get("relatedGoals", []))] for r in D["interests"]])

CATS = [("old_background", "bg_old"), ("recent_background", "bg_recent"), ("pseudonym", "pseudonyms"), ("perk", "perks")]
hdr = ["category", "id", "name", "cost"] + [f"skill_{s}" for s in SK] + [
    "interests", "attribute_modifiers", "conflicts_with", "only_for", "age_min", "age_max", "religious_alignment", "description"]
all_rows = []
for cat, k in CATS:
    rows = []
    for r in D[k]:
        sm = {s: 0 for s in SK}
        for m in r.get("skillModifiers", []):
            sm[m["key"]] += m["value"]
        n = tx(lk(r).get("name"), pretty(r["id"])); n = n[:1].upper() + n[1:]
        ar = r.get("ageRange") or {}
        rows.append([cat, r["id"], n, r["creationPointCost"]] + [sm[s] for s in SK] + [
            ";".join(f'{g["key"]}={g["value"]}' for g in r.get("goalPreferences", [])),
            ";".join(f'{a["key"]}={a["value"]}' for a in r.get("attributeModifiers", [])),
            ";".join(r.get("conflictsWith", [])),
            ";".join(CHT.get(x, str(x)) for x in r.get("ignoreCharacteristicType", [])),
            ar.get("min", ""), ar.get("max", ""), r.get("religiousAlignment", ""), tx(lk(r).get("info"), "")])
    write(f"{cat}s.csv", hdr, rows)
    all_rows += rows
write("all_options.csv", hdr, all_rows)

role_rows = []
for r in D["roles"]:
    for l in r["roleLevels"]:
        role_rows.append([r["id"], tx(lk(r).get("name"), r["id"]), l["level"],
                          ";".join(f'{ENUM.get(q["key"], q["key"])}>={q["value"]}' for q in l.get("skillRequirements") or []),
                          l.get("hoursRequirement", ""), ";".join(l.get("roomRequirement", [])),
                          ";".join(f'{g["key"]}={g["value"]}' for g in l.get("goalPreferences", [])),
                          tx(lk(r).get("description"), "")])
write("roles.csv", ["role_id", "name", "level", "skill_requirements", "hours_requirement", "room_requirement", "interests", "description"], role_rows)

write("generation_rules.csv", ["rule", "value", "note"], [
    ["maxCreationPoints", G["maxCreationPoints"], "creation-point budget per settler"],
    ["baseSkillCount", G["baseSkillCount"], "base skill pool (exact use is in compiled code)"],
    ["pseudonymChance", G["pseudonymChance"], "% chance a random settler gets a pseudonym"],
    ["ageMin", G["ageRange"]["min"], ""], ["ageMax", G["ageRange"]["max"], ""]] +
    [[f'perks_age_{a["ageCategory"]["min"]}_{a["ageCategory"]["max"]}', f'{a["possiblePerks"]["min"]}-{a["possiblePerks"]["max"]}', "perks allowed by age"]
     for a in G["ageCategories"]] +
    [[f"passions_tier{i + 1}", f'{p["min"]}-{p["max"]}', "interest/passion counts on generation (tiers unverified)"]
     for i, p in enumerate(G["passions"])])

missing = sorted(k for k, v in _cache.items() if v is None)
print(f"localization: {len(_cache) - len(missing)}/{len(_cache)} keys resolved; missing (ID used instead): {missing}")
