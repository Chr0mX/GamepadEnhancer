#!/usr/bin/env python3
"""Builds the static pages: python3 site/build.py

Each page body lives in site/src/<page>.html and starts with a meta comment:
    <!--meta title="..." description="..." nav="Label" order="1"-->
site/src/layout.html wraps it (header, nav, footer). Output goes to site/<page>.html.
Only the Python standard library is used.
"""
import pathlib, re, sys

ROOT = pathlib.Path(__file__).resolve().parent
SRC = ROOT / "src"
EXTERNAL = [("GitHub", "https://github.com/Chr0mX/GamepadEnhancer")]

def parse_meta(text):
    m = re.match(r"\s*<!--meta (.*?)-->\s*", text, re.S)
    if not m:
        raise SystemExit("missing <!--meta ...--> header")
    meta = dict(re.findall(r'(\w+)="(.*?)"', m.group(1)))
    return meta, text[m.end():]

def main():
    layout = (SRC / "layout.html").read_text(encoding="utf-8")
    pages = []
    for f in sorted(SRC.glob("*.html")):
        if f.name == "layout.html":
            continue
        meta, body = parse_meta(f.read_text(encoding="utf-8"))
        pages.append((int(meta.get("order", "99")), f.name, meta, body))
    pages.sort(key=lambda p: p[0])
    for _, name, meta, body in pages:
        nav = []
        for _, other, om, _b in pages:
            cur = ' aria-current="page"' if other == name else ""
            nav.append(f'        <li><a href="{other}"{cur}>{om["nav"]}</a></li>')
        for label, url in EXTERNAL:
            nav.append(f'        <li><a href="{url}" rel="noopener">{label}</a></li>')
        html = (layout.replace("{{title}}", meta["title"]).replace("{{description}}", meta["description"])
                .replace("{{nav}}", "\n".join(nav)).replace("{{content}}", body.rstrip()))
        (ROOT / name).write_text(html, encoding="utf-8")
        print("built", name)
    (ROOT / ".nojekyll").write_text("", encoding="utf-8")

if __name__ == "__main__":
    sys.exit(main())
