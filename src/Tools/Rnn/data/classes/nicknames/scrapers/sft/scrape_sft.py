import argparse
import concurrent.futures
import re
import threading
import time
from pathlib import Path
from typing import Iterable, Optional, Tuple

try:
    # Python 3
    from urllib.request import Request, urlopen
    from urllib.error import URLError, HTTPError
except ImportError:  # pragma: no cover
    # Fallback for very old Pythons, not expected in this project
    from urllib2 import Request, urlopen  # type: ignore
    from urllib2 import URLError, HTTPError  # type: ignore


TITLE_REGEX = re.compile(r"<title[^>]*>(.*?)</title>", re.IGNORECASE | re.DOTALL)
MISSING_TITLE_PHRASE = "stránka, na kterou se odkazujete, byla pravděpodobně přesunuta"
INACTIVE_TITLE = "neaktivní uživatel"


def parse_title(html: str) -> Optional[str]:
    match = TITLE_REGEX.search(html)
    if not match:
        return None
    title = match.group(1)
    # Normalize whitespace
    title = re.sub(r"\s+", " ", title).strip()
    return title or None


def is_missing_page_title(title: str) -> bool:
    return MISSING_TITLE_PHRASE in title.lower()


def is_inactive_user_title(title: str) -> bool:
    return title.strip().lower() == INACTIVE_TITLE


def fetch_title_for_id(item_id: int, timeout: float, retries: int, backoff_base: float, user_agent: str) -> Tuple[int, Optional[str], Optional[str]]:
    """
    Returns (id, title, error). If error is not None, title may be None.
    """
    url = f"https://www.itnetwork.cz/portfolio/{item_id}"
    last_error: Optional[str] = None
    for attempt in range(retries + 1):
        try:
            req = Request(url, headers={
                "User-Agent": user_agent,
                "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
                "Accept-Language": "en-US,en;q=0.9",
                "Connection": "keep-alive",
            })
            with urlopen(req, timeout=timeout) as resp:
                raw = resp.read()
            # Try utf-8 first, fallback to latin-1
            try:
                html = raw.decode("utf-8", errors="replace")
            except Exception:
                html = raw.decode("latin-1", errors="replace")
            title = parse_title(html)
            if title is None:
                return item_id, None, "no-title"
            if is_missing_page_title(title):
                return item_id, None, "missing"
            if is_inactive_user_title(title):
                return item_id, None, "inactive"
            return item_id, title, None
        except HTTPError as e:
            try:
                body = e.read().decode("utf-8", errors="replace")
            except Exception:
                body = ""
            title = parse_title(body) if body else None
            # Even on HTTP errors, capturing the title (e.g., error page) is useful
            if title:
                if is_missing_page_title(title):
                    return item_id, None, "missing"
                if is_inactive_user_title(title):
                    return item_id, None, "inactive"
                return item_id, title, None
            last_error = f"HTTPError {e.code}"
        except URLError as e:
            last_error = f"URLError {getattr(e, 'reason', e)}"
        except Exception as e:  # noqa: BLE001
            last_error = f"Exception {e.__class__.__name__}: {e}"

        # Backoff before retrying
        if attempt < retries:
            sleep_s = backoff_base * (2 ** attempt)
            time.sleep(sleep_s)

    return item_id, None, last_error or "unknown-error"


def iter_ranges(start: int, end: int) -> Iterable[int]:
    return range(start, end + 1)


def read_done_ids(done_path: Path, legacy_output_with_ids: Path) -> set:
    done: set = set()
    # Preferred: dedicated done-ids file
    if done_path.exists():
        try:
            with done_path.open("r", encoding="utf-8", errors="ignore") as f:
                for line in f:
                    try:
                        done.add(int(line.strip()))
                    except Exception:
                        continue
            return done
        except Exception:
            pass
    # Fallback: legacy mode where output started with id<TAB>...
    if legacy_output_with_ids.exists():
        try:
            with legacy_output_with_ids.open("r", encoding="utf-8", errors="ignore") as f:
                for line in f:
                    if not line.strip():
                        continue
                    parts = line.split("\t", 1)
                    try:
                        done.add(int(parts[0]))
                    except Exception:
                        # names-only lines won't parse; ignore
                        continue
        except Exception:
            return set()
    return done


def main():
    parser = argparse.ArgumentParser(description="Scrape itnetwork portfolio titles into sft_raw.txt")
    parser.add_argument("--start", type=int, default=1, help="Start ID (inclusive)")
    parser.add_argument("--end", type=int, default=145000, help="End ID (inclusive)")
    parser.add_argument("--workers", type=int, default=64, help="Number of threads")
    parser.add_argument("--timeout", type=float, default=10.0, help="Per-request timeout in seconds")
    parser.add_argument("--retries", type=int, default=2, help="Retries per request on failure")
    parser.add_argument("--rate-delay", type=float, default=0.0, help="Optional sleep between task submissions (seconds)")
    parser.add_argument("--out", type=str, default="sft_raw.txt", help="Output filename (written next to this script)")
    parser.add_argument("--resume", action="store_true", help="Resume by skipping IDs already in output file")
    parser.add_argument("--progress-interval", type=float, default=5.0, help="Seconds between progress logs")
    args = parser.parse_args()

    base_dir = Path(__file__).parent
    output_path = base_dir / args.out
    done_ids_path = base_dir / f"{args.out}.done_ids"
    output_path.parent.mkdir(parents=True, exist_ok=True)

    user_agent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36"

    already_done = read_done_ids(done_ids_path, output_path) if args.resume else set()
    ids_to_process = [i for i in iter_ranges(args.start, args.end) if i not in already_done]
    total_to_do = len(ids_to_process)

    print(f"Scraping IDs {args.start}..{args.end} | workers={args.workers} | resume={args.resume} | to_do={total_to_do}")

    write_lock = threading.Lock()
    processed_counter = 0
    saved_counter = 0
    processed_lock = threading.Lock()
    saved_lock = threading.Lock()
    t_start = time.time()

    stop_event = threading.Event()

    def progress_logger():
        while not stop_event.wait(args.progress_interval):
            with processed_lock:
                processed = processed_counter
            with saved_lock:
                saved = saved_counter
            elapsed = time.time() - t_start
            rate = processed / max(1.0, elapsed)
            remaining = max(0, total_to_do - processed)
            eta = remaining / max(1e-6, rate)
            print(f"Progress: {processed}/{total_to_do} processed, {saved} saved | {rate:.1f} req/s | ETA ~{eta/60:.1f} min")

    def handle_result(result: Tuple[int, Optional[str], Optional[str]]):
        nonlocal processed_counter, saved_counter
        item_id, title, error = result
        # Append to done-ids file if resume is enabled
        if args.resume:
            with write_lock:
                with done_ids_path.open("a", encoding="utf-8") as df:
                    df.write(f"{item_id}\n")

        # Skip writing for known missing pages or any error
        if error in {"missing", "inactive"} or title is None:
            with processed_lock:
                processed_counter += 1
            return

        # Write only the name (trimmed)
        line = f"{title.strip()}\n"
        with write_lock:
            with output_path.open("a", encoding="utf-8") as f:
                f.write(line)
        with saved_lock:
            saved_counter += 1
        with processed_lock:
            processed_counter += 1
        with processed_lock:
            processed_counter += 1
            if processed_counter % 1000 == 0:
                elapsed = time.time() - t_start
                rate = processed_counter / max(1.0, elapsed)
                remaining = total_to_do - processed_counter
                eta = remaining / max(1e-6, rate)
                print(f"Processed {processed_counter}/{total_to_do} | {rate:.1f} req/s | ETA ~{eta/60:.1f} min")

    # Start periodic progress logger
    logger_thread = threading.Thread(target=progress_logger, daemon=True)
    logger_thread.start()

    with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as executor:
        futures = []
        for item_id in ids_to_process:
            future = executor.submit(
                fetch_title_for_id,
                item_id,
                args.timeout,
                args.retries,
                0.2,  # backoff base seconds
                user_agent,
            )
            future.add_done_callback(lambda fut: handle_result(fut.result()))
            futures.append(future)
            if args.rate_delay > 0:
                time.sleep(args.rate_delay)

        # Wait for all to complete
        for fut in concurrent.futures.as_completed(futures):
            pass

    # Stop progress logger and print final stats
    stop_event.set()
    if logger_thread.is_alive():
        logger_thread.join(timeout=1.0)

    elapsed = time.time() - t_start
    print(f"Done. Wrote names to {output_path}. Took {elapsed/60:.1f} minutes. Saved {saved_counter} names.")


if __name__ == "__main__":
    main()


