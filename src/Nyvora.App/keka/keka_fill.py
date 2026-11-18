#!/usr/bin/env python3
"""
Fills one day on your Keka "All Timesheets" page (the weekly grid). It NEVER submits the week.

What it does, the way you do it by hand:
  1. opens the timesheet address (Edge, with its own saved sign-in),
  2. adds the project / work item row with "+ Add Time Entry" if this week does not have it yet,
  3. types the hours in that day's cell and the comment (Keka requires one),
  4. presses Save (only with --save) and waits for "Timesheet saved successfully".
"Submit weekly timesheet" is never clicked.

Exit codes (Nyvora reads these):
  0  done            10  sign in needed      11  page changed (something was not found)
  13 something else  14  playwright is not installed

Needs the playwright package. Nyvora installs it into its own Python environment the first time (no browser download: it uses Edge).
"""
import argparse
import datetime
import json
import pathlib
import re
import sys
import time
from urllib.parse import urlparse

OK, SIGN_IN, CHANGED, FAILED, NO_PLAYWRIGHT = 0, 10, 11, 13, 14
SIGNED_OUT_WORDS = ("login", "signin", "sign-in", "microsoftonline", "accounts.google", "okta", "/auth", "sso")
MONTHS = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"]
WEEKDAYS = ["MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN"]


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


def hours_text(hours):
    """8 -> '8', 7.5 -> '7:30' (Keka shows hours as H:MM)."""
    h = float(hours)
    whole, minutes = int(h), int(round((h - int(h)) * 60))
    return str(whole) if minutes == 0 else f"{whole}:{minutes:02d}"


def day_header(page, date):
    """The column header cell for the date ('18 NOV' over 'WED'): the smallest visible element holding both."""
    # No word boundary after the month: the date and the weekday can run together in the page text ("18 NOVWED").
    pattern = re.compile(rf"(?<!\d)0?{date.day}\s*{MONTHS[date.month - 1]}", re.I)
    cells = page.locator("div,th,td,span,p").filter(has_text=pattern).filter(has_text=re.compile(WEEKDAYS[date.weekday()], re.I))
    best = None
    for i in range(min(cells.count(), 150)):
        el = cells.nth(i)
        try:
            if not el.is_visible():
                continue
            box = el.bounding_box()
        except Exception:
            continue
        if not box or box["width"] < 20 or box["width"] > 260:
            continue
        area = box["width"] * box["height"]
        if best is None or area < best[0]:
            best = (area, el)
    if best is None:
        return None
    best[1].scroll_into_view_if_needed()  # the grid scrolls sideways
    return best[1].bounding_box()


def work_item_row(page, text, below_y):
    """The visible work item title in the grid (not the dropdown): its box, or None."""
    found = page.get_by_text(text, exact=False)
    for i in range(min(found.count(), 20)):
        el = found.nth(i)
        try:
            if not el.is_visible():
                continue
            box = el.bounding_box()
        except Exception:
            continue
        if box and box["y"] > below_y:
            return box
    return None


def inventory(page):
    try:
        return page.evaluate(
            """() => [...document.querySelectorAll('input,textarea,select,button')]
                .filter(e => e.offsetParent !== null)
                .map(e => [e.tagName, e.type || '', e.getAttribute('aria-label') || '', e.placeholder || '',
                           (e.innerText || '').trim().slice(0, 40)].join(' | ')).slice(0, 60)"""
        )
    except Exception:
        return []


def changed(page, log, args, why):
    shot = pathlib.Path(args.log_dir) / "keka-last.png"
    try:
        page.screenshot(path=str(shot))
    except Exception:
        pass
    log(why)
    log("Things on the page:")
    for line in inventory(page):
        log("   " + line)
    return finish(CHANGED, log, f"Screenshot: {shot}")


def fill_day(page, entry, fields, args, log, quiet=False):
    date = datetime.date.fromisoformat(entry["date"])
    project, item = entry.get("project", ""), entry.get("task", "")
    if not project or not item:
        return changed(page, log, args, "No project or work item given (set them in Nyvora Settings).")

    header = day_header(page, date)
    if header is None:
        return changed(page, log, args, f"The column for {date:%d %b} is not on the page. Open the week that holds it and try again.")
    below = header["y"] + header["height"]

    row = work_item_row(page, item, below)
    if row is None:
        log(f"No '{item}' row this week; adding it")
        page.get_by_text(fields["add_entry"], exact=False).first.click()
        search = page.get_by_placeholder(fields["project_search_placeholder"], exact=True).first
        search.wait_for(state="visible", timeout=8000)
        search.fill(project)
        time.sleep(1)
        page.get_by_text(project, exact=True).first.click()
        time.sleep(1)
        page.get_by_text(item, exact=False).first.click()
        for _ in range(20):
            time.sleep(0.5)
            row = work_item_row(page, item, below)
            if row:
                break
        if row is None:
            return changed(page, log, args, f"Chose '{project}' / '{item}' but no row appeared.")
    header = day_header(page, date) or header  # the grid may have shifted

    x = header["x"] + header["width"] / 2
    y = row["y"] + row["height"] / 2
    page.mouse.click(x, y)
    time.sleep(0.4)
    page.keyboard.press("Control+A")
    page.keyboard.type(hours_text(entry["hours"]))
    log(f"Typed {hours_text(entry['hours'])} h for {date:%a %d %b}")

    comment = page.get_by_placeholder(re.compile(fields["comment_placeholder"], re.I)).first
    try:
        comment.wait_for(state="visible", timeout=6000)
    except Exception:
        return changed(page, log, args, "The comment box did not appear after typing the hours.")
    comment.fill(entry["comment"])
    log("Typed the comment")
    try:
        page.get_by_text("All Timesheets", exact=True).first.click()  # click away so Keka takes the values
    except Exception:
        pass
    time.sleep(0.5)

    if not args.save:
        # No Save pressed: leave the window open (this run is not headless) so you can check it and press Save yourself.
        print("RESULT 0", flush=True)
        try:
            page.context.wait_for_event("close", timeout=30 * 60 * 1000)
        except Exception:
            pass
        return OK

    name = fields["save_button"]
    if name.lower() in [n.lower() for n in fields["never_click"]]:
        return changed(page, log, args, "Refusing to click a button on the never-click list.")
    save = page.get_by_role("button", name=name, exact=True)
    if save.count() == 0:
        return changed(page, log, args, "Filled, but there is no Save button.")
    save.first.click()
    log(f"Clicked '{name}'")
    try:
        page.get_by_text(re.compile(fields["saved_text"], re.I)).first.wait_for(state="visible", timeout=10000)
    except Exception:
        return changed(page, log, args, "Clicked Save but did not see Keka confirm it.")
    log("Keka confirmed the save (still not submitted).")
    return OK


def launch(p, args, headless):
    kw = dict(channel="msedge", headless=headless, args=["--no-first-run"])
    if args.browser_path:  # tests only
        kw = dict(executable_path=args.browser_path, headless=headless, args=["--no-sandbox"])
    if not headless:
        kw["no_viewport"] = True
    ctx = p.chromium.launch_persistent_context(args.profile, **kw)
    page = ctx.pages[0] if ctx.pages else ctx.new_page()
    page.set_default_timeout(10000)
    page.goto(args.url, wait_until="domcontentloaded", timeout=45000)
    try:
        page.wait_for_load_state("networkidle", timeout=15000)
    except Exception:
        pass
    time.sleep(2)  # the page is a single-page app: give it a moment to draw
    return ctx, page


def wait_for_sign_in(page, host, log):
    log("Waiting for you to sign in (up to 10 minutes)...")
    end = time.time() + 600
    while time.time() < end:
        if not signed_out(page, host):
            time.sleep(2)  # let the session cookies settle
            return True
        time.sleep(2)
    return False


def attempt(p, args, fields, entry, host, log, headless):
    """One fill. Returns OK, SIGN_IN, CHANGED or FAILED (never raises)."""
    ctx = None
    try:
        ctx, page = launch(p, args, headless)
        if signed_out(page, host):
            log(f"Not signed in (landed on {page.url}).")
            return SIGN_IN
        log(f"On {page.url}; filling {entry.get('date')}")
        return fill_day(page, entry, fields, args, log, quiet=True)
    except Exception as ex:  # Edge missing, profile in use, ...
        log(f"{type(ex).__name__}: {ex}")
        return FAILED
    finally:
        if ctx is not None:
            try:
                ctx.close()
            except Exception:
                pass


def show_window(p, args, log, sign_in_host=None):
    """Open a visible Edge window on the page. With a host: wait for the sign in and say whether it happened."""
    ctx = None
    try:
        ctx, page = launch(p, args, headless=False)
        if sign_in_host:
            return wait_for_sign_in(page, sign_in_host, log)
        try:  # leave it open so you can look, sign in or fix things yourself
            ctx.wait_for_event("close", timeout=30 * 60 * 1000)
        except Exception:
            pass
        return True
    except Exception as ex:
        log(f"Could not open a window: {type(ex).__name__}: {ex}")
        return False
    finally:
        if ctx is not None:
            try:
                ctx.close()
            except Exception:
                pass


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--url", required=True)
    ap.add_argument("--profile", required=True)
    ap.add_argument("--log-dir", required=True)
    ap.add_argument("--entry")
    ap.add_argument("--login", action="store_true", help="open Edge so you can sign in, then stop")
    ap.add_argument("--save", action="store_true", help="also press Save (a draft; never Submit). Without it the window stays open.")
    ap.add_argument("--browser-path", help=argparse.SUPPRESS)
    args = ap.parse_args()
    log = Log(args.log_dir)

    try:
        from playwright.sync_api import sync_playwright
    except ImportError:
        return finish(NO_PLAYWRIGHT, log, "playwright is not installed in this Python. (Nyvora normally sets this up itself.)")

    host = urlparse(args.url).hostname
    fields = json.loads(pathlib.Path(__file__).with_name("keka_fields.json").read_text(encoding="utf-8"))
    entry = json.loads(pathlib.Path(args.entry).read_text(encoding="utf-8")) if args.entry else {}
    entry = {str(k).lower(): v for k, v in entry.items()}  # the app writes lower-case names; accept any casing

    try:
        with sync_playwright() as p:
            if args.login:
                return finish(OK if show_window(p, args, log, host) else SIGN_IN, log,
                              "Sign in finished.")

            # Quietly in the background when it saves by itself; with a window when you asked to review and press Save.
            code = attempt(p, args, fields, entry, host, log, headless=args.save)

            if code == SIGN_IN:
                log("Signed out: opening a window so you can sign in, then trying again.")
                if show_window(p, args, log, host):
                    code = attempt(p, args, fields, entry, host, log, headless=args.save)

            if code == OK:
                return finish(OK, log, "Filled. Nyvora never submits the week: do that yourself when it is ready.")

            # Something is wrong: say so first, then open a window on the page so you can see it.
            result = finish(code, log, "Something went wrong; opening a window on the page.")
            if code in (CHANGED, FAILED, SIGN_IN):
                show_window(p, args, log)
            return result
    except Exception as ex:
        return finish(FAILED, log, f"{type(ex).__name__}: {ex}")


if __name__ == "__main__":
    sys.exit(main())
