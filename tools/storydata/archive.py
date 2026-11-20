"""Polite, cached access to Liquipedia pages as kept by the Internet Archive (ADR 0004).

We never contact liquipedia.net. Everything goes through web.archive.org's CDX index and its
"id_" snapshot mode (the page exactly as captured), at about one request per second, with an
identifying User-Agent, retries with backoff, and an on-disk cache so nothing is fetched twice.
"""
from __future__ import annotations

import gzip
import hashlib
import json
import time
import urllib.parse
from pathlib import Path

import requests

USER_AGENT = "EM2026-StoryMode-data/0.1 (personal non-commercial game mod; reads Internet Archive copies)"
CACHE = Path(__file__).parent / "cache"
MIN_INTERVAL = 1.1  # seconds between requests to the Archive

_session = requests.Session()
_session.headers.update({"User-Agent": USER_AGENT, "Accept-Encoding": "gzip"})
_last = 0.0


def _wait() -> None:
    global _last
    delay = MIN_INTERVAL - (time.monotonic() - _last)
    if delay > 0:
        time.sleep(delay)
    _last = time.monotonic()


def get(url: str, *, params: dict | None = None, retries: int = 6) -> requests.Response:
    """GET with rate limiting and exponential backoff on throttling / server errors."""
    for attempt in range(retries):
        _wait()
        try:
            r = _session.get(url, params=params, timeout=60)
        except requests.RequestException:
            r = None
        if r is not None and r.status_code == 200:
            return r
        if r is not None and r.status_code == 404:
            r.raise_for_status()
        time.sleep(min(300, 5 * 2 ** attempt))  # 429 / 5xx / network: back off
    raise RuntimeError(f"giving up on {url} after {retries} attempts")


def _cache_path(kind: str, key: str) -> Path:
    h = hashlib.sha1(key.encode("utf-8")).hexdigest()
    return CACHE / kind / h[:2] / f"{h}.json"


def page(original_url: str, *, before: str = "2026") -> dict | None:
    """The latest archived copy of a Liquipedia page (at or before `before`, a yyyy[mm[dd]] stamp).

    Returns {"url", "captured", "html"} or None if the Archive has no copy. Cached on disk.
    """
    path = _cache_path("pages", f"{original_url}|{before}")
    if path.exists():
        data = json.loads(path.read_text(encoding="utf-8"))
        return data or None
    snapshot = f"https://web.archive.org/web/{before}id_/{original_url}"
    try:
        r = get(snapshot)
    except requests.HTTPError:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("{}", encoding="utf-8")
        return None
    raw = r.content
    # id_ returns the bytes as captured; many captures are themselves gzip-encoded.
    if raw[:2] == b"\x1f\x8b":
        raw = gzip.decompress(raw)
    html = raw.decode("utf-8", "replace")
    captured = r.url.split("/web/")[1].split("id_")[0] if "/web/" in r.url else ""
    data = {"url": original_url, "captured": captured, "html": html}
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data), encoding="utf-8")
    return data


def cdx(params: dict) -> list[list[str]]:
    """One CDX query (JSON output, first row is the header)."""
    r = get("https://web.archive.org/cdx/search/cdx", params={**params, "output": "json"})
    return r.json() if r.text.strip() else []


def title_of(url: str) -> str:
    """Page title from a liquipedia.net/counterstrike/... URL."""
    path = urllib.parse.urlsplit(url).path
    return urllib.parse.unquote(path.split("/counterstrike/", 1)[-1]).replace("_", " ")
