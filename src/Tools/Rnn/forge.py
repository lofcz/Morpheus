import os
import sys
import io
import argparse
import time
import random
import heapq
import hashlib
import shutil
from typing import Iterable, Iterator, List, Tuple, Optional, Dict

# Resolve paths relative to this script's directory by default
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))


Label = str
MixtureEntry = Tuple[str, float]


def _supports_color() -> bool:
    return sys.stdout.isatty() and os.environ.get("NO_COLOR") is None


class C:
    if _supports_color():
        RESET = "\033[0m"
        BOLD = "\033[1m"
        DIM = "\033[2m"
        RED = "\033[91m"
        GREEN = "\033[92m"
        YELLOW = "\033[93m"
        BLUE = "\033[94m"
        MAGENTA = "\033[95m"
        CYAN = "\033[96m"
    else:
        RESET = ""
        BOLD = ""
        DIM = ""
        RED = ""
        GREEN = ""
        YELLOW = ""
        BLUE = ""
        MAGENTA = ""
        CYAN = ""


def normalize_line(text: str) -> Optional[str]:
    """
    Normalize a line for consistent deduplication:
    - strip leading/trailing whitespace
    - collapse internal whitespace to a single space
    - casefold (Unicode-safe lowercasing)
    Returns None for empty lines after normalization.
    """
    s = text.strip()
    if not s:
        return None
    # Collapse any internal whitespace (spaces, tabs) to a single space
    s = " ".join(s.split())
    # Case-insensitive dedup across sources
    s = s.casefold()
    if not s:
        return None
    return s


def parse_mixture_file(mixture_path: str) -> Tuple[Dict[Label, List[MixtureEntry]], str]:
    """
    Parse mixture.txt which contains lines in the form:
      labelType,relative_or_absolute_file_path,float(optional)

    Supported label types (case-insensitive, singular/plural accepted):
      name(s), company(ies), nickname(s), handle(s)

    Returns a mapping of normalized labels -> list of (absolute_path, fraction)
    and the base directory of the mixture file.
    """
    base_dir = os.path.dirname(os.path.abspath(mixture_path))
    groups: Dict[Label, List[MixtureEntry]] = {"name": [], "company": [], "nickname": [], "city": []}

    def normalize_label(label: str) -> Optional[str]:
        l = label.strip().lower()
        if l.endswith("ies"):
            l = l[:-3] + "y"  # companies -> company
        elif l.endswith("s"):
            l = l[:-1]  # names -> name, nicknames -> nickname
        if l in ("name", "company", "nickname", "handle", "city"):
            if l == "handle": return "nickname"
            return l
        return None

    with io.open(mixture_path, "r", encoding="utf-8", errors="ignore") as f:
        for line_no, raw in enumerate(f, start=1):
            line = raw.strip()
            if not line or line.startswith("#"):
                continue
            parts = [p.strip() for p in line.split(",")]
            if len(parts) < 2:
                print(f"{C.YELLOW}[mixture]{C.RESET} Skipping malformed line {line_no}: {raw.rstrip()}")
                continue
            label_raw, path_raw = parts[0], parts[1]
            label = normalize_label(label_raw)
            if label is None:
                print(f"{C.YELLOW}[mixture]{C.RESET} Skipping unknown label '{label_raw}' on line {line_no}")
                continue
            fraction = 1.0
            if len(parts) >= 3 and parts[2] != "":
                try:
                    fraction = float(parts[2])
                except ValueError:
                    print(f"{C.YELLOW}[mixture]{C.RESET} Invalid fraction on line {line_no}, defaulting to 1.0")
                    fraction = 1.0
            if fraction <= 0.0:
                # Nothing to sample from this file
                continue
            path = path_raw
            if not os.path.isabs(path):
                path = os.path.abspath(os.path.join(base_dir, path))
            groups[label].append((path, fraction))

    return groups, base_dir


def iter_source_lines(path: str, fraction: float, seed: int) -> Iterator[str]:
    """
    Stream lines from a source file, probabilistically sampling by 'fraction'.
    Each line is normalized. Empty lines after normalization are skipped.
    """
    # Path-specific RNG for reproducibility (stable across processes)
    path_abs = os.path.abspath(path)
    h = hashlib.blake2b(path_abs.encode("utf-8"), digest_size=8).digest()
    path_hash = int.from_bytes(h, "little") & 0xFFFFFFFF
    rng = random.Random((seed & 0xFFFFFFFF) ^ path_hash)

    with io.open(path, "r", encoding="utf-8", errors="ignore") as f:
        for raw in f:
            if fraction < 1.0 and rng.random() >= fraction:
                continue
            s = normalize_line(raw)
            if s:
                yield s


def write_chunk(lines: List[str], temp_dir: str, prefix: str, chunk_index: int) -> str:
    lines.sort()
    out_path = os.path.join(temp_dir, f"{prefix}.chunk{chunk_index:05d}.txt")
    with io.open(out_path, "w", encoding="utf-8", newline="\n") as out:
        prev: Optional[str] = None
        for s in lines:
            if s != prev:
                out.write(s)
                out.write("\n")
                prev = s
    return out_path


def create_sorted_chunks(
    iterable: Iterable[str], temp_dir: str, prefix: str, chunk_size: int, log_every: int
) -> List[str]:
    os.makedirs(temp_dir, exist_ok=True)
    chunk_paths: List[str] = []
    buffer: List[str] = []
    total = 0
    chunk_idx = 0
    last_log = time.time()
    for s in iterable:
        buffer.append(s)
        total += 1
        if log_every and total % log_every == 0:
            now = time.time()
            rate = log_every / max(1e-6, (now - last_log))
            print(f"{C.CYAN}[chunk:{prefix}]{C.RESET} read={total:,} ~{rate:,.0f} lines/s; chunks={len(chunk_paths)}")
            last_log = now
        if len(buffer) >= chunk_size:
            path = write_chunk(buffer, temp_dir, prefix, chunk_idx)
            chunk_paths.append(path)
            chunk_idx += 1
            buffer = []
    if buffer:
        path = write_chunk(buffer, temp_dir, prefix, chunk_idx)
        chunk_paths.append(path)
    print(f"{C.CYAN}[chunk:{prefix}]{C.RESET} total read={total:,}; wrote {len(chunk_paths)} sorted chunk(s)")
    return chunk_paths


def merge_sorted_files(paths: List[str]) -> Iterator[str]:
    files = [io.open(p, "r", encoding="utf-8", errors="strict") for p in paths]
    heap: List[Tuple[str, int]] = []
    for i, f in enumerate(files):
        s = f.readline()
        if s:
            heap.append((s.rstrip("\n"), i))
    heapq.heapify(heap)
    prev: Optional[str] = None
    try:
        while heap:
            s, i = heapq.heappop(heap)
            if s != prev:
                yield s
                prev = s
            nxt = files[i].readline()
            if nxt:
                heapq.heappush(heap, (nxt.rstrip("\n"), i))
    finally:
        for f in files:
            f.close()


def iter_file_lines(path: str) -> Iterator[str]:
    with io.open(path, "r", encoding="utf-8", errors="strict") as f:
        for raw in f:
            yield raw.rstrip("\n")


def next_or_none(it: Iterator[str]) -> Optional[str]:
    try:
        return next(it)
    except StopIteration:
        return None


def subtract_streams(a_iter: Iterator[str], b_iter: Iterator[str]) -> Iterator[str]:
    b = next_or_none(b_iter)
    for a in a_iter:
        while b is not None and b < a:
            b = next_or_none(b_iter)
        if b == a:
            continue
        yield a


def subtract_two_streams(a_iter: Iterator[str], b_iter: Iterator[str], c_iter: Iterator[str]) -> Iterator[str]:
    b = next_or_none(b_iter)
    c = next_or_none(c_iter)
    for a in a_iter:
        while b is not None and b < a:
            b = next_or_none(b_iter)
        if b == a:
            continue
        while c is not None and c < a:
            c = next_or_none(c_iter)
        if c == a:
            continue
        yield a


def write_stream_to_file(stream: Iterator[str], out_path: str, log_every: int) -> int:
    count = 0
    last_log = time.time()
    with io.open(out_path, "w", encoding="utf-8", newline="\n") as out:
        for s in stream:
            out.write(s)
            out.write("\n")
            count += 1
            if log_every and count % log_every == 0:
                now = time.time()
                rate = log_every / max(1e-6, (now - last_log))
                print(f"{C.MAGENTA}[write:{os.path.basename(out_path)}]{C.RESET} written={count:,} ~{rate:,.0f} lines/s")
                last_log = now
    return count


def process_category(
    label: Label,
    sources: List[MixtureEntry],
    temp_dir: str,
    chunk_size: int,
    log_every: int,
    seed: int,
) -> List[str]:
    """
    Create sorted unique chunks for a category from its sources.
    Returns a list of temporary chunk file paths.
    """
    if not sources:
        return []

    def source_iter() -> Iterator[str]:
        for path, fraction in sources:
            if not os.path.exists(path):
                print(f"{C.YELLOW}[{label}]{C.RESET} WARNING: source not found: {path}")
                continue
            print(f"{C.BLUE}[{label}]{C.RESET} reading: {path} (fraction={fraction})")
            yield from iter_source_lines(path, fraction, seed)

    return create_sorted_chunks(source_iter(), temp_dir, label, chunk_size, log_every)


def main() -> int:
    parser = argparse.ArgumentParser(description="Forge consolidated class files from a mixture of sources with scalable deduplication.")
    parser.add_argument("--mixture", default=os.path.join(SCRIPT_DIR, "data", "classes", "mixture.txt"), help="Path to mixture.txt")
    parser.add_argument("--out-dir", default=os.path.join(SCRIPT_DIR, "data", "classes"), help="Output directory containing names.txt, companies.txt, nicknames.txt")
    parser.add_argument("--temp-dir", default=os.path.join(SCRIPT_DIR, "data", "_forge_tmp"), help="Directory for temporary chunk files")
    parser.add_argument("--chunk-size", type=int, default=2_000_000, help="Number of lines per in-memory sort chunk")
    parser.add_argument("--log-every", type=int, default=1_000_000, help="Log progress every N lines")
    parser.add_argument("--seed", type=int, default=42, help="Random seed for sampling fractions")
    args = parser.parse_args()

    start_time = time.time()
    groups, mixture_base = parse_mixture_file(args.mixture)
    out_dir = os.path.abspath(args.out_dir)
    os.makedirs(out_dir, exist_ok=True)
    temp_dir = os.path.abspath(args.temp_dir)
    os.makedirs(temp_dir, exist_ok=True)

    names_path = os.path.join(out_dir, "names.txt")
    companies_path = os.path.join(out_dir, "companies.txt")
    nicknames_path = os.path.join(out_dir, "nicknames.txt")
    cities_path = os.path.join(out_dir, "cities.txt")

    # Clean output files (truncate)
    for p in (names_path, companies_path, nicknames_path, cities_path):
        with io.open(p, "w", encoding="utf-8"):
            pass

    # 1) Names – produce final sorted unique file
    print(f"\n{C.CYAN}=== Stage 1: {C.BOLD}Names{C.RESET}{C.CYAN} ==={C.RESET}")
    name_chunks = process_category("name", groups.get("name", []), temp_dir, args.chunk_size, args.log_every, args.seed)
    if name_chunks:
        names_stream = merge_sorted_files(name_chunks)
        names_written = write_stream_to_file(names_stream, names_path, args.log_every)
        print(f"{C.GREEN}[names]{C.RESET} total written: {names_written:,}")
    else:
        print(f"{C.YELLOW}[names]{C.RESET} No sources; file will remain empty")

    # Cleanup name chunks
    for p in name_chunks:
        try:
            os.remove(p)
        except OSError:
            pass

    # 2) Companies – unique, then subtract names
    print(f"\n{C.CYAN}=== Stage 2: {C.BOLD}Companies{C.RESET}{C.CYAN} (excluding names) ==={C.RESET}")
    company_chunks = process_category("company", groups.get("company", []), temp_dir, args.chunk_size, args.log_every, args.seed)
    if company_chunks:
        companies_unique_stream = merge_sorted_files(company_chunks)
        names_iter_for_companies = iter_file_lines(names_path)
        companies_final_stream = subtract_streams(companies_unique_stream, names_iter_for_companies)
        companies_written = write_stream_to_file(companies_final_stream, companies_path, args.log_every)
        print(f"{C.GREEN}[companies]{C.RESET} total written (after excluding names): {companies_written:,}")
    else:
        print(f"{C.YELLOW}[companies]{C.RESET} No sources; file will remain empty")

    for p in company_chunks:
        try:
            os.remove(p)
        except OSError:
            pass

    # 3) Nicknames – unique, then subtract names and companies
    print(f"\n{C.CYAN}=== Stage 3: {C.BOLD}Nicknames{C.RESET}{C.CYAN} (excluding names and companies) ==={C.RESET}")
    nickname_chunks = process_category("nickname", groups.get("nickname", []), temp_dir, args.chunk_size, args.log_every, args.seed)
    if nickname_chunks:
        nicknames_unique_stream = merge_sorted_files(nickname_chunks)
        names_iter_for_nicks = iter_file_lines(names_path)
        companies_iter_for_nicks = iter_file_lines(companies_path)
        nicknames_final_stream = subtract_two_streams(nicknames_unique_stream, names_iter_for_nicks, companies_iter_for_nicks)
        nicknames_written = write_stream_to_file(nicknames_final_stream, nicknames_path, args.log_every)
        print(f"{C.GREEN}[nicknames]{C.RESET} total written (after excluding names+companies): {nicknames_written:,}")
    else:
        print(f"{C.YELLOW}[nicknames]{C.RESET} No sources; file will remain empty")

    for p in nickname_chunks:
        try:
            os.remove(p)
        except OSError:
            pass

    # 4) Cities – produce final sorted unique file (no subtraction needed)
    print(f"\n{C.CYAN}=== Stage 4: {C.BOLD}Cities{C.RESET}{C.CYAN} ==={C.RESET}")
    city_chunks = process_category("city", groups.get("city", []), temp_dir, args.chunk_size, args.log_every, args.seed)
    if city_chunks:
        cities_stream = merge_sorted_files(city_chunks)
        cities_written = write_stream_to_file(cities_stream, cities_path, args.log_every)
        print(f"{C.GREEN}[cities]{C.RESET} total written: {cities_written:,}")
    else:
        print(f"{C.YELLOW}[cities]{C.RESET} No sources; file will remain empty")

    for p in city_chunks:
        try:
            os.remove(p)
        except OSError:
            pass

    # Cleanup temp directory if empty
    try:
        if os.path.isdir(temp_dir) and not os.listdir(temp_dir):
            os.rmdir(temp_dir)
    except OSError:
        pass

    elapsed = time.time() - start_time
    print(f"\n{C.GREEN}{C.BOLD}All stages completed in{C.RESET} {elapsed:,.1f}s")
    print("Outputs:")
    print(f" {C.DIM}- {names_path}{C.RESET}")
    print(f" {C.DIM}- {companies_path}{C.RESET}")
    print(f" {C.DIM}- {nicknames_path}{C.RESET}")
    print(f" {C.DIM}- {cities_path}{C.RESET}")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        print("Interrupted.")
        sys.exit(130)


