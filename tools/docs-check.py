"""Checks the built documentation site: links, anchors, Liquid leftovers, todo stubs, shots in another language,
and the sources for long dashes."""
import html.parser
import pathlib
import re
import sys
import urllib.parse

SITE = pathlib.Path(sys.argv[1])
SOURCES = pathlib.Path(sys.argv[2]) if len(sys.argv) > 2 else None
BASE = "/amneziageo_server"
DASHES = re.compile("[‒–—―−]")


class Page(html.parser.HTMLParser):
    def __init__(self):
        super().__init__()
        self.ids = set()
        self.links = []
        self.todos = 0
        self.fallbacks = 0

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if "id" in attrs:
            self.ids.add(attrs["id"])
        if tag == "a" and "href" in attrs:
            self.links.append(attrs["href"])
        if tag == "img" and "src" in attrs:
            self.links.append(attrs["src"])
        if tag == "figure" and "data-shot-fallback" in attrs:
            self.fallbacks += 1
        if tag == "link" and "href" in attrs:
            self.links.append(attrs["href"])
        if tag == "p" and "todo" in (attrs.get("class") or "").split():
            self.todos += 1


pages = {}
for path in SITE.rglob("*.html"):
    parser = Page()
    text = path.read_text(encoding="utf-8")
    parser.feed(text)
    url = "/" + path.relative_to(SITE).as_posix()
    pages[url] = (parser, text)


def resolve(url):
    if url.endswith("/"):
        url += "index.html"
    if url in pages:
        return url
    if (SITE / url.lstrip("/")).exists():
        return url
    return None


problems = []
todos = {"ru": 0, "en": 0}
fallbacks = {"ru": 0, "en": 0}
for url, (parser, text) in sorted(pages.items()):
    todos["ru" if url.endswith(".ru.html") else "en"] += parser.todos
    fallbacks["ru" if url.endswith(".ru.html") else "en"] += parser.fallbacks
    for leftover in re.findall(r"\{%.*?%\}|\{\{.*?\}\}", text):
        problems.append(f"{url}: liquid leftover {leftover}")
    for href in parser.links:
        parts = urllib.parse.urlsplit(href)
        if parts.scheme or href.startswith("mailto:"):
            continue
        target = parts.path
        if not target:
            target_url = url
        elif target.startswith("/"):
            if not target.startswith(BASE + "/") and target != BASE:
                problems.append(f"{url}: outside baseurl {href}")
                continue
            target_url = target[len(BASE):] or "/"
        else:
            target_url = urllib.parse.urljoin(url, target)
        resolved = resolve(urllib.parse.unquote(target_url))
        if not resolved:
            problems.append(f"{url}: missing page {href}")
            continue
        if parts.fragment and resolved in pages:
            fragment = urllib.parse.unquote(parts.fragment)
            if fragment not in pages[resolved][0].ids:
                problems.append(f"{url}: missing anchor {href}")

dashes = 0
if SOURCES:
    for path in sorted(SOURCES.rglob("*")):
        if path.suffix not in {".md", ".yml", ".html"} or any(part.startswith(".") for part in path.parts):
            continue
        for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            if DASHES.search(line):
                dashes += 1
                problems.append(f"{path.relative_to(SOURCES).as_posix()}:{number}: long dash")

print("\n".join(problems))
for url, (parser, _) in sorted(pages.items()):
    if parser.todos:
        print(f"{url}: {parser.todos} todo")
print(f"{len(pages)} pages, {len(problems)} problems, todo ru {todos['ru']} en {todos['en']}, "
      f"shots in another language ru {fallbacks['ru']} en {fallbacks['en']}, long dashes {dashes}")
