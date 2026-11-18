#!/usr/bin/env python3
"""
Fills one day's entry on your Keka timesheet page. It NEVER submits the timesheet.

It drives the Microsoft Edge you already have installed, with its own saved profile (kept in Nyvora's data folder), so
you sign in once and later runs reuse it. If anything is unexpected it stops and says why instead of guessing.

Exit codes (Nyvora reads these):
  0  done            10  sign in needed      11  page changed (a field was not found)
  13 something else  14  playwright is not installed (pip install playwright)

Needs the playwright package. Nyvora installs it into its own Python environment the first time (no browser download: it uses Edge).
"""
import argparse
import datetime
import json
import pathlib
import sys
import time
from urllib.parse import urlparse

OK, SIGN_IN, CHANGED, FAILED, NO_PLAYWRIGHT = 0, 10, 11, 13, 14
SIGNED_OUT_WORDS = ("login", "signin", "sign-in", "microsoftonline", "accounts.google", "okta", "/auth", "sso")


class Log:
    def __init__(self, folder):
        self.path = pathlib.Path(folder) / "keka.log"
        self.path.parent.mkdir(parents=True, exist_ok=True)

    def __call__(self, msg):
        line = f"{datetime.datetime.now():%Y-%m-%d %H:%M:%S} {msg}"
        print(line, flush=True)
        try:
            with self.path.open("a", encoding="utf-8") as f:
                f.write(line + "\n")
        except OSError:
            pass


def finish(code, log, note=""):
    if note:
        log(note)
    print(f"RESULT {code}", flush=True)
    return code


def signed_out(page, host):
    url = page.url.lower()
    if urlparse(page.url).hostname != host or any(w in url for w in SIGNED_OUT_WORDS):
        return True
    try:
        return page.locator("input[type=password]").first.is_visible(timeout=1500)
    except Exception:
        return False


def find(page, names, kinds=("textbox", "combobox", "spinbutton")):
    """First visible control whose label, placeholder or accessible name contains one of the names."""
    for name in names:
        candidates = [page.get_by_label(name, exact=False), page.get_by_placeholder(name, exact=False)]
        candidates += [page.get_by_role(k, name=name) for k in kinds]
        for c in candidates:
            try:
                if c.count() and c.first.is_visible():
                    return c.first
            except Exception:
                continue
    return None


def put(page, control, text):
    tag = control.evaluate("e => e.tagName.toLowerCase()")
    if tag == "select":
        control.select_option(label=text)
        return
    control.click()
    try:
        control.fill(text)
    except Exception:
        page.keyboard.press("Control+A")
        page.keyboard.type(text)
    # Dropdown-style fields offer a list: pick the matching option if one appears.
    try:
        option = page.get_by_role("option", name=text, exact=False).first
        if option.is_visible(timeout=800):
            option.click()
    except Exception:
        pass


def inventory(page):
    try:
        return page.evaluate(
            """() => [...document.querySelectorAll('input,textarea,select,button')]
                .filter(e => e.offsetParent !== null)
                .map(e => [e.tagName, e.type || '', e.getAttribute('aria-label') || '', e.placeholder || '',
                           e.name || '', (e.innerText || '').trim().slice(0, 40)].join(' | ')).slice(0, 60)"""
        )
    except Exception:
        return []


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--url", required=True)
    ap.add_argument("--profile", required=True)
    ap.add_argument("--log-dir", required=True)
    ap.add_argument("--entry")
    ap.add_argument("--login", action="store_true", help="open Edge so you can sign in, then stop")
    ap.add_argument("--save", action="store_true", help="also click Save on the day's entry (never Submit)")
    args = ap.parse_args()
    log = Log(args.log_dir)

    try:
        from playwright.sync_api import sync_playwright
    except ImportError:
        return finish(NO_PLAYWRIGHT, log, "playwright is not installed in this Python. (Nyvora normally sets this up itself.)")

    host = urlparse(args.url).hostname
    fields = json.loads((pathlib.Path(__file__).with_name("keka_fields.json")).read_text(encoding="utf-8"))
    entry = json.loads(pathlib.Path(args.entry).read_text(encoding="utf-8")) if args.entry else {}

    try:
        with sync_playwright() as p:
            ctx = p.chromium.launch_persistent_context(
                args.profile, channel="msedge", headless=False, no_viewport=True, args=["--no-first-run"])
            page = ctx.pages[0] if ctx.pages else ctx.new_page()
            try:
                page.goto(args.url, wait_until="domcontentloaded", timeout=45000)
                try:
                    page.wait_for_load_state("networkidle", timeout=15000)
                except Exception:
                    pass
                time.sleep(2)  # the page is a single-page app: give it a moment to draw

                if args.login:
                    log("Waiting for you to sign in (up to 10 minutes)...")
                    end = time.time() + 600
                    while time.time() < end:
                        if not signed_out(page, host):
                            time.sleep(2)  # let the session cookies settle
                            return finish(OK, log, "Signed in. The session is saved.")
                        time.sleep(2)
                    return finish(SIGN_IN, log, "Timed out waiting for sign in.")

                if signed_out(page, host):
                    return finish(SIGN_IN, log, f"Not signed in (landed on {page.url}).")

                log(f"On {page.url}; filling {entry.get('date')}")
                plan = [("hours", f"{float(entry['hours']):g}" if entry.get("hours") is not None else ""),
                        ("project", entry.get("project", "")), ("task", entry.get("task", "")),
                        ("comment", entry.get("comment", ""))]
                for key, text in plan:
                    if not text:
                        continue
                    control = find(page, fields[key])
                    if control is None:
                        shot = pathlib.Path(args.log_dir) / "keka-last.png"
                        try:
                            page.screenshot(path=str(shot))
                        except Exception:
                            pass
                        log(f"Could not find the '{key}' field (tried {fields[key]}). Fields on the page:")
                        for line in inventory(page):
                            log("   " + line)
                        return finish(CHANGED, log, f"Screenshot: {shot}")
                    put(page, control, str(text))
                    log(f"Filled {key}")

                if args.save:
                    never = [n.lower() for n in fields["never_click"]]
                    for name in fields["save_buttons"]:
                        if name.lower() in never:
                            continue
                        button = page.get_by_role("button", name=name, exact=True)
                        if button.count() and button.first.is_visible():
                            button.first.click()
                            log(f"Clicked '{name}'")
                            break
                    else:
                        return finish(CHANGED, log, "Filled, but found no Save button.")

                code = finish(OK, log, "Filled. Review it in the Edge window; Nyvora never submits.")
                if not args.save:
                    try:  # keep the window open so you can check it and press Save yourself
                        ctx.wait_for_event("close", timeout=30 * 60 * 1000)
                    except Exception:
                        pass
                return code
            finally:
                try:
                    ctx.close()
                except Exception:
                    pass
    except Exception as ex:  # Edge missing, profile in use, window closed by hand, ...
        return finish(FAILED, log, f"{type(ex).__name__}: {ex}")


if __name__ == "__main__":
    sys.exit(main())
