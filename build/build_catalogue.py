#!/usr/bin/env python3
"""
Step 2 of the icon build: turn icons/assets into the bundled catalogue.

For each icon folder under icons/assets:
  - normalise the SVG (see normalise_svg) so it follows Microsoft's guidance
    for Dataverse SVG web resources: currentColor fills, width/height 16,
    viewBox kept from the source artwork, no style/class/script/event attributes,
    under 10 KB
  - collect name, keywords (metadata metaphor + keyword fields) and categories
    (from build/categories.json)

Outputs:
  src/Oliver4.IconLibrary/Web/catalogue.json.gz   (embedded in the DLL)
  build/out/catalogue.json                                  (readable copy)
  build/out/category-report.txt                             (counts and 'Other' list for review)

Usage:  python3 build/build_catalogue.py
No third-party dependencies.
"""
import gzip, io, json, os, re, sys, collections
import xml.etree.ElementTree as ET

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(REPO, "icons", "assets")
OUT_WEB = os.path.join(REPO, "src", "Oliver4.IconLibrary", "Web", "catalogue.json.gz")
OUT_DIR = os.path.join(REPO, "build", "out")
MAX_BYTES = 10 * 1024
SVG_NS = "http://www.w3.org/2000/svg"

ET.register_namespace("", SVG_NS)


def tag(el):
    return el.tag.split("}", 1)[1] if "}" in el.tag else el.tag


def normalise_svg(source, size):
    """Return (normalised svg text, list of problems). Problems are fatal for the build."""
    problems = []
    root = ET.fromstring(source)
    if tag(root) != "svg":
        return None, ["root element is not <svg>"]

    viewbox = root.get("viewBox") or f"0 0 {size} {size}"
    out = ET.Element(f"{{{SVG_NS}}}svg")
    out.set("width", "16")
    out.set("height", "16")
    out.set("viewBox", viewbox)
    out.set("fill", "currentColor")

    def is_full_clip(defs_el):
        """A <clipPath> whose only child is a rect covering the whole viewBox clips nothing."""
        for cp in defs_el:
            if tag(cp) != "clipPath":
                return False
            kids = list(cp)
            if len(kids) != 1 or tag(kids[0]) != "rect":
                return False
            r = kids[0]
            if r.get("x", "0") != "0" or r.get("y", "0") != "0":
                return False
            if r.get("width") != str(size) or r.get("height") != str(size):
                return False
        return True

    def walk(src_el, dst_parent):
        for child in src_el:
            t = tag(child)
            if t in ("script", "style", "title", "desc", "metadata"):
                if t in ("script", "style"):
                    problems.append(f"<{t}> element present")
                continue
            if t == "defs":
                if is_full_clip(child):
                    continue  # no-op clip, drop it
                problems.append("<defs> with content other than a full-size clipPath")
                continue
            if t == "g":
                # flatten groups; a clip-path pointing at the dropped full-size clip is a no-op
                walk(child, dst_parent)
                continue
            new = ET.SubElement(dst_parent, f"{{{SVG_NS}}}{t}")
            for k, v in child.attrib.items():
                kl = k.lower()
                if kl in ("class", "style", "id", "clip-path") or kl.startswith("on") or kl.startswith("xlink"):
                    if kl == "style" or kl.startswith("on"):
                        problems.append(f"attribute {k} present")
                    continue
                if kl == "fill":
                    if v.strip().lower() == "none":
                        new.set("fill", "none")
                    # any other fill (hex, named, currentColor) is inherited from the root currentColor
                    continue
                if kl == "stroke":
                    if v.strip().lower() not in ("none", "currentcolor"):
                        new.set("stroke", "currentColor")
                    else:
                        new.set("stroke", v)
                    continue
                new.set(k, v)
            walk(child, new)

    walk(root, out)
    if len(list(out)) == 0:
        problems.append("no drawable elements")

    text = ET.tostring(out, encoding="unicode")
    # compact: single line, no xml declaration
    text = re.sub(r">\s+<", "><", text).strip()
    if re.search(r"#[0-9a-fA-F]{3,8}\b", text):
        problems.append("hard-coded colour remains")
    if len(text.encode("utf-8")) > MAX_BYTES:
        problems.append(f"{len(text.encode('utf-8'))} bytes exceeds 10 KB")
    return text, problems


def icon_id(svg_filename):
    m = re.fullmatch(r"ic_fluent_(.+)_(16|20)_regular\.svg", svg_filename)
    return m.group(1), int(m.group(2))


def load_categories():
    with open(os.path.join(REPO, "build", "categories.json"), encoding="utf-8") as f:
        data = json.load(f)
    cats = []
    for c in data["categories"]:
        cats.append({
            "id": c["id"], "label": c["label"],
            "name": set(t.lower() for t in c.get("name", [])),
            "metaphor": set(t.lower() for t in c.get("metaphor", [])),
        })
    return cats


def categorise(name_words, metaphors, cats):
    hits = [c["id"] for c in cats if name_words & c["name"]]
    if not hits:
        hits = [c["id"] for c in cats if metaphors & c["metaphor"]]
    return hits or ["other"]


def main():
    cats = load_categories()
    icons = []
    errors = []
    counts = collections.Counter()
    other = []
    for folder in sorted(os.listdir(ASSETS)):
        d = os.path.join(ASSETS, folder)
        if not os.path.isdir(d):
            continue
        svgs = [f for f in os.listdir(d) if f.endswith(".svg")]
        if len(svgs) != 1:
            errors.append(f"{folder}: expected one svg, found {len(svgs)}")
            continue
        with open(os.path.join(d, "metadata.json"), encoding="utf-8") as f:
            meta = json.load(f)
        iid, size = icon_id(svgs[0])
        with open(os.path.join(d, svgs[0]), encoding="utf-8") as f:
            src = f.read()
        svg, problems = normalise_svg(src, size)
        if problems:
            errors.append(f"{folder}: " + "; ".join(problems))
            continue
        name = meta.get("name") or folder
        metaphors = [m.strip().lower() for m in (meta.get("metaphor") or []) if m and m.strip()]
        extra_kw = [k.strip().lower() for k in (meta.get("keyword") or "").split(",") if k.strip() and k.strip() != "fluent-icon"]
        name_words = set(name.lower().split())
        categories = categorise(name_words, set(metaphors), cats)
        keywords = sorted(set(metaphors + extra_kw))
        icons.append({
            "id": iid,                # e.g. "person_add" - used for the default web resource name
            "name": name,             # e.g. "Person Add"
            "size": size,             # 16 or 20: which Fluent artwork this came from
            "keywords": keywords,
            "categories": categories,
            "svg": svg,
        })
        for c in categories:
            counts[c] += 1
        if categories == ["other"]:
            other.append(f"{name}  [{', '.join(metaphors)}]")

    if errors:
        print("Build failed, fix these icons or adjust normalise_svg:", file=sys.stderr)
        for e in errors:
            print("  " + e, file=sys.stderr)
        sys.exit(1)

    with open(os.path.join(REPO, "icons", "SOURCE.json"), encoding="utf-8") as f:
        source = json.load(f)

    catalogue = {
        "source": {
            "repository": source["repository"],
            "commit": source["commit"],
            "commitDate": source["commitDate"],
            "licence": "MIT",
        },
        "categories": [{"id": c["id"], "label": c["label"], "count": counts[c["id"]]} for c in cats]
                      + [{"id": "other", "label": "Other", "count": counts["other"]}],
        "icons": icons,
    }

    os.makedirs(os.path.dirname(OUT_WEB), exist_ok=True)
    os.makedirs(OUT_DIR, exist_ok=True)
    raw = json.dumps(catalogue, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    with gzip.GzipFile(OUT_WEB, "wb", mtime=0) as gz:
        gz.write(raw)
    with open(os.path.join(OUT_DIR, "catalogue.json"), "w", encoding="utf-8") as f:
        json.dump(catalogue, f, ensure_ascii=False, indent=1)

    with open(os.path.join(OUT_DIR, "category-report.txt"), "w", encoding="utf-8") as f:
        f.write(f"Icons: {len(icons)} (16px: {sum(1 for i in icons if i['size']==16)}, 20px resized: {sum(1 for i in icons if i['size']==20)})\n")
        f.write(f"Catalogue: {len(raw)/1024:.0f} KB raw, {os.path.getsize(OUT_WEB)/1024:.0f} KB gzipped\n\n")
        f.write("Category counts:\n")
        for c in catalogue["categories"]:
            f.write(f"  {c['label']:<32}{c['count']:>6}\n")
        f.write(f"\nIcons in Other ({len(other)}):\n")
        for o in other:
            f.write("  " + o + "\n")
    print(f"{len(icons)} icons, {len(raw)//1024} KB raw, {os.path.getsize(OUT_WEB)//1024} KB gzipped -> {OUT_WEB}")
    print(f"Other: {len(other)}. See {os.path.join(OUT_DIR, 'category-report.txt')}")


if __name__ == "__main__":
    main()
