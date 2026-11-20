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
MIN_INTERVAL = 1.5  # seconds between requests to the Archive

_session = requests.Session()
_session.headers.update({"User-Agent": USER_AGENT, "Accept-Encoding": "gzip"})
_last = 0.0


def _wait() -> None:
    global _last
    delay = MIN_INTERVAL - (time.monotonic() - _last)
    if delay > 0:
        time.sleep(delay)
    _last = time.monotonic()


class FetchFailed(RuntimeError):
    """The Archive kept failing for one URL; the caller decides whether to retry later."""


def get(url: str, *, params: dict | None = None, retries: int = 5) -> requests.Response:
    """GET with rate limiting; backs off on throttling (429, honouring Retry-After) and server errors."""
    last = "no response"
    for attempt in range(retries):
        _wait()
        try:
            r = _session.get(url, params=params, timeout=90)
        except requests.RequestException as e:
            r, last = None, type(e).__name__
        if r is not None:
            if r.status_code == 200:
                return r
            if r.status_code in (403, 404):
                r.raise_for_status()  # permanent for this URL: no point retrying
            last = f"HTTP {r.status_code}"
            if r.status_code == 429:
                wait = int(r.headers.get("Retry-After", "0") or 0)
                time.sleep(max(60, wait))
                continue
        time.sleep(min(120, 5 * 2 ** attempt))
    raise FetchFailed(f"{url}: {last} after {retries} attempts")


def _cache_path(kind: str, key: str) -> Path:
    h = hashlib.sha1(key.encode("utf-8")).hexdigest()
    return CACHE / kind / h[:2] / f"{h}.json"


def page(original_url: str, *, before: str = "2026", stamp: str | None = None,
         captured_url: str | None = None) -> dict | None:
    """The latest archived copy of a Liquipedia page (at or before `before`, a yyyy[mm[dd]] stamp).

    With `stamp` (and the URL exactly as captured), the copy is fetched directly: one cheap request.
    Without it the Archive picks the capture nearest to `before`. Returns {"url", "captured",
    "html"} or None if the Archive has no copy. Cached on disk.
    """
    path = _cache_path("pages", f"{original_url}|{before}")
    if path.exists():
        data = json.loads(path.read_text(encoding="utf-8"))
        return data or None
    r = None
    if stamp:
        try:
            r = get(f"https://web.archive.org/web/{stamp}id_/{captured_url or original_url}")
        except requests.HTTPError:
            r = None
    try:
        if r is None:
            r = get(f"https://web.archive.org/web/{before}id_/{original_url}")
    except requests.HTTPError:
        # The capture nearest to `before` can be an archived error (Liquipedia refused the
        # Archive's crawler: 403). Fall back to the latest successful capture.
        r = None
        rows = cdx({"url": original_url, "filter": "statuscode:200", "fl": "timestamp",
                    "to": before, "limit": "-1"})
        stamps = [row[0] for row in rows[1:] if row] if rows else []
        if stamps:
            try:
                r = get(f"https://web.archive.org/web/{stamps[-1]}id_/{original_url}")
            except requests.HTTPError:
                r = None
        if r is None:
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


def binary(original_url: str) -> bytes | None:
    """An archived file (an image) as bytes: the latest successful capture. Cached on disk."""
    path = _cache_path("files", original_url).with_suffix(".bin")
    if path.exists():
        data = path.read_bytes()
        return data or None
    rows = cdx({"url": original_url, "filter": "statuscode:200", "fl": "timestamp", "limit": "-1"})
    stamps = [row[0] for row in rows[1:] if row] if rows else []
    data = b""
    if stamps:
        try:
            data = get(f"https://web.archive.org/web/{stamps[-1]}id_/{original_url}").content
        except requests.HTTPError:
            data = b""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)
    return data or None


def cdx(params: dict) -> list[list[str]]:
    """One CDX query (JSON output, first row is the header)."""
    r = get("https://web.archive.org/cdx/search/cdx", params={**params, "output": "json"})
    return r.json() if r.text.strip() else []


def title_of(url: str) -> str:
    """Page title from a liquipedia.net/counterstrike/... URL."""
    path = urllib.parse.urlsplit(url).path
    return urllib.parse.unquote(path.split("/counterstrike/", 1)[-1]).replace("_", " ")
