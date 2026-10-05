#!/usr/bin/env python3
"""
Step 1 of the icon build: copy the Regular 16 (or 20 fallback) SVG and a trimmed
metadata.json for every icon from a checkout of microsoft/fluentui-system-icons
into icons/assets/<Icon Name>/ in this repo.

Usage:  python3 build/extract_icons.py <path-to-fluentui-system-icons-checkout>

Only the Regular style is kept. Sizes other than the one kept are removed from the
metadata so the repo copy only describes what ships. LICENSE is copied to icons/
so the licence travels with the assets.
"""
import json, os, re, shutil, subprocess, sys

src = sys.argv[1]
repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
dest = os.path.join(repo, "icons", "assets")
os.makedirs(dest, exist_ok=True)

kept16 = kept20 = skipped = 0
skipped_names = []
for name in sorted(os.listdir(os.path.join(src, "assets"))):
    folder = os.path.join(src, "assets", name)
    svgdir = os.path.join(folder, "SVG")
    meta_path = os.path.join(folder, "metadata.json")
    if not os.path.isdir(svgdir) or not os.path.isfile(meta_path):
        continue
    with open(meta_path, encoding="utf-8") as f:
        meta = json.load(f)
    files = os.listdir(svgdir)
    chosen = size = None
    for s in (16, 20):
        cand = [x for x in files if re.fullmatch(rf"ic_fluent_.+_{s}_regular\.svg", x)]
        if cand:
            chosen, size = cand[0], s
            break
    if not chosen:
        skipped += 1; skipped_names.append(name); continue
    out = os.path.join(dest, name)
    os.makedirs(out, exist_ok=True)
    shutil.copyfile(os.path.join(svgdir, chosen), os.path.join(out, chosen))
    trimmed = {
        "name": meta.get("name", name),
        "size": [size],
        "style": ["Regular"],
        "keyword": meta.get("keyword", ""),
        "description": meta.get("description", ""),
        "metaphor": meta.get("metaphor", []),
    }
    with open(os.path.join(out, "metadata.json"), "w", encoding="utf-8") as f:
        json.dump(trimmed, f, indent=2, ensure_ascii=False)
        f.write("\n")
    if size == 16: kept16 += 1
    else: kept20 += 1

shutil.copyfile(os.path.join(src, "LICENSE"), os.path.join(repo, "icons", "LICENSE"))

commit = subprocess.run(["git", "-C", src, "rev-parse", "HEAD"], capture_output=True, text=True).stdout.strip()
date = subprocess.run(["git", "-C", src, "log", "-1", "--format=%cs"], capture_output=True, text=True).stdout.strip()
with open(os.path.join(repo, "icons", "SOURCE.json"), "w") as f:
    json.dump({"repository": "https://github.com/microsoft/fluentui-system-icons",
               "commit": commit, "commitDate": date,
               "icons16": kept16, "icons20": kept20, "skippedNoRegular16or20": skipped_names}, f, indent=2)
print(f"16px: {kept16}  20px fallback: {kept20}  skipped (no Regular 16/20): {skipped}")
print("skipped:", ", ".join(skipped_names[:40]), "..." if len(skipped_names) > 40 else "")
