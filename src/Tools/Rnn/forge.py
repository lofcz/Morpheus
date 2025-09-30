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
import unicodedata
import re

# Resolve paths relative to this script's directory by default
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))


Label = str
MixtureEntry = Tuple[str, float]

# --- Title stripping for names (moved from remix) ---
# Comprehensive lists of titles
TITLES_BEFORE = [
    "Pan", "Paní", "Bc.", "BcA.", "Ing.", "Ing. arch.", "MUDr.", "MDDr.", "MVDr.",
    "MgA.", "Mgr.", "JUDr.", "PhDr.", "RNDr.", "PharmDr.", "ThLic.", "ThDr.",
    "akad. arch.", "ak. mal.", "ak. soch.", "MSDr.", "PaedDr.", "PhMr.", "RCDr.",
    "RSDr.", "RTDr.", "ThMgr.", "as.", "odb. as.", "doc.", "prof.", "voj.",
    "svob.", "sv.", "des.", "čet.", "rtn.", "rtm.", "nrtm.", "prap.", "nprap.",
    "št. prap.", "šprap.", "por.", "npor.", "kpt.", "mjr.", "pplk.", "plk.",
    "brig.gen.", "genmjr.", "genpor.", "arm.gen.", "ppor.", "škpt.", "šrtm.",
    "gen.", "genplk.", "Rev.", "Very Rev.", "Most Rev.", "Rt. Rev.", "Right Rev.",
    "Fr.", "Father", "Sister", "Br.", "Brother", "Dcn.", "Deacon", "Bp.", "Bishop",
    "Abp.", "Archbishop", "Msgr.", "Monsignor", "Card.", "Cardinal", "Dom", "Abbot",
    "Mother", "Pastor", "Padre",
    # Promovaný titles
    "prom.", "promovaný", "promovaná",
    "prom. lékař", "promovaný lékař", "promovaná lékařka",
    "prom. právník", "promovaný právník", "promovaná právnička",
    "prom. biolog", "promovaný biolog", "promovaná bioložka",
    "prom. historik", "promovaný historik", "promovaná historička",
    "inženýr architekt", "prom. architekt", "promovaný architekt", "promovaná architektka",
    "prom. matematik", "promovaný matematik", "promovaná matematička",
    "prom. fyzik", "promovaný fyzik", "promovaná fyzička",
    "prom. chemik", "promovaný chemik", "promovaná chemička",
    "prom. geolog", "promovaný geolog", "promovaná geoložka",
    "prom. filosof", "promovaný filosof", "promovaná filosofka",
    "prom. psycholog", "promovaný psycholog", "promovaná psycholožka",
    "prom. ekonom", "promovaný ekonom", "promovaná ekonomka",
    "prom. farmaceut", "promovaný farmaceut", "promovaná farmaceutka",
    "prom. umělec", "promovaný umělec", "promovaná umělkyně",
    "prom. pedagog", "promovaný pedagog", "promovaná pedagožka",
    "prom. sociolog", "promovaný sociolog", "promovaná socioložka",
]
TITLES_AFTER = [
    "Ph.D.", "DSc.", "CSc.", "Dr.", "DrSc.", "Th.D.", "DiS.", "dr. h. c.",
    "prof. h. c.", "MBA", "LL.M.", "Jr.", "Sr.", "PP.", "J.Em.", "J.Exc.",
    "J.M.", "Vdp.", "AMPLMUS", "A.R.D.", "Vldp.", "R.D.", "Dp.", "Vp.",
    "Rev. dom.", "Ct.p.", "V.G.", "P.A.", "J.C.D.", "S.T.D.", "D.D.", "Dr. eccl."
]

def _normalize_dotless_token(s: str) -> str:
    # Lowercase, strip dots and diacritics
    s2 = s.strip().lower().replace('.', '')
    s2 = s2 if s2 else s
    s2 = unicodedata.normalize('NFD', s2)
    return ''.join(ch for ch in s2 if not unicodedata.combining(ch))

TITLE_NORM_SET = {
    _normalize_dotless_token(tok)
    for t in (TITLES_BEFORE + TITLES_AFTER)
    for tok in t.split()
    if tok
}

def strip_titles_from_name_line(raw: str) -> str:
    parts = raw.split()
    kept: List[str] = []
    for p in parts:
        pn = _normalize_dotless_token(p)
        if pn in TITLE_NORM_SET:
            continue
        kept.append(p)
    return ' '.join(kept).strip()

# --- Additional name cleanup rules ---

# Character filtering (Czech, Slovak, Polish, Russian, English only)
_LATIN_BASE = "abcdefghijklmnopqrstuvwxyz"
_CS = "áčďéěíňóřšťúůýž"
_SK = "áäčďéíĺľňóôŕšťúýž"
_PL = "ąćęłńóśźż"
_RUS_CYR_RANGE = ("\u0430", "\u044f")  # 'а'..'я'
_RUS_EXTRA = "ё"
_ALLOWED_LATIN = set(_LATIN_BASE + _CS + _SK + _PL)
_ALLOWED_CYR = set(chr(c) for c in range(ord(_RUS_CYR_RANGE[0]), ord(_RUS_CYR_RANGE[1]) + 1)) | set(_RUS_EXTRA)
_ALLOWED_CHARS = _ALLOWED_LATIN | _ALLOWED_CYR | set(" -")  # Only letters, space, hyphen at this stage

def _filter_allowed_chars(s: str) -> str:
    """Keep only Czech, Slovak, Polish, Russian, English letters plus space and hyphen."""
    return ''.join(ch for ch in s if ch in _ALLOWED_CHARS)

_DASH_MAP = {
    "–": "-",
    "—": "-",
    "−": "-",
}

_QUOTE_PAIRS = [
    ("\"", "\""), ("'", "'"), ("“", "”"), ("„", "“"), ("‹", "›"), ("«", "»"), ("`", "`")
]

def _normalize_dashes(s: str) -> str:
    return ''.join(_DASH_MAP.get(ch, ch) for ch in s)

def _remove_quoted_segments(s: str) -> str:
    # Remove substrings inside any supported quote pair
    for ql, qr in _QUOTE_PAIRS:
        start = s.find(ql)
        while start != -1:
            end = s.find(qr, start + len(ql))
            if end == -1:
                # No closing quote; drop from opening quote
                s = s[:start]
                break
            s = s[:start] + s[end + len(qr):]
            start = s.find(ql)
    return s

_SEP_CHARS = [".", ",", "&", "#", "@", ";"]

_LETTER_RE = re.compile(r"[A-Za-zÀ-ÖØ-öø-ÿĀ-žŽ]+", re.UNICODE)

def _is_compound_two_word_hyphen(s: str) -> bool:
    # e.g., "Jan-Novák" (no spaces, exactly two letter sequences joined by a single hyphen)
    if " " in s:
        return False
    parts = s.split("-")
    if len(parts) != 2:
        return False
    return bool(_LETTER_RE.fullmatch(parts[0]) and _LETTER_RE.fullmatch(parts[1]))

def _cut_after_separators(s: str) -> str:
    # Find first separator index with special handling for hyphens
    first_idx = len(s)

    # Hyphen: treat as separator only if spaced (e.g., " - ", " -X", "X- ")
    hyphen_idx = -1
    for i, ch in enumerate(s):
        if ch != '-':
            continue
        left = s[i-1] if i > 0 else ' '
        right = s[i+1] if i + 1 < len(s) else ' '
        if left.isspace() or right.isspace():
            hyphen_idx = i
            break
    if hyphen_idx != -1:
        first_idx = min(first_idx, hyphen_idx)

    # Other separators
    for ch in _SEP_CHARS:
        i = s.find(ch)
        if i != -1:
            first_idx = min(first_idx, i)

    if first_idx != len(s):
        s = s[:first_idx]
    return s

def clean_name_line(raw: str) -> Optional[str]:
    # Apply title stripping first
    s = strip_titles_from_name_line(raw)
    if not s:
        return None
    # Normalize to lowercase first
    s = s.lower()
    
    # Quick checks before expensive operations
    # Discard if contains any digits (early exit)
    if any(ch.isdigit() for ch in s):
        return None
    
    # Normalize dashes
    s = _normalize_dashes(s)
    # Filter to allowed characters only (Czech, Slovak, Polish, Russian, English)
    s = _filter_allowed_chars(s)
    if not s:
        return None
    # Remove anything in quotes
    s = _remove_quoted_segments(s)
    # Specific leftover title case: ing.arch.
    s = re.sub(r"\bing\.arch\.?\b", " ", s, flags=re.IGNORECASE)
    # Remove everything after first separator (with hyphen exception)
    s = _cut_after_separators(s)
    # Trim and strip trailing punctuation that may remain (preserve internal hyphens)
    s = s.strip()
    s = re.sub(r"[\s\.,;&#@]+$", "", s)
    
    # Discard if too short or empty (early exit)
    if not s or len(s) < 3:
        return None
    
    # Normalize diacritics once for all token-based checks
    norm = unicodedata.normalize('NFD', s)
    norm = ''.join(ch for ch in norm if not unicodedata.combining(ch))
    
    # Split once and reuse
    tokens = norm.split()
    if not tokens:
        return None
    
    # Discard if first word is a single character followed by more content
    if len(tokens) >= 2 and len(tokens[0]) == 1:
        return None
    
    # Discard if starts with only "a" as first word
    if tokens[0] == "a" and len(tokens) >= 1:
        return None
    
    # Discard if standalone conjunction "a" appears anywhere
    if 'a' in tokens:
        return None
    
    # Fast substring checks for occupational/medical markers
    if any(k in norm for k in (
        "kancelar", "advokat", "doktor", "lekar", "zverolekar",
        "zdravotnicke", "zdravotni", "zarizeni", "ordinace",
        "fitnes", "komunita", "biuro", "bizarre", "ambulace", "cinnost"
    )):
        return None
    
    # Token-based checks (set lookup is faster than repeated 'in' checks)
    blocked_tokens = {
        "lekar", "zverolekar", "lekarstvi", "lekarka", "lekaru",
        "sdruzeni", "predseda", "ceska", "cesky", "armada", "institut",
        "je", "diplom", "firma", "biuro", "bizarre", "ambulace", "cinnost"
    }
    if any(t in blocked_tokens for t in tokens):
        return None
    
    # Discard if starts with "fit"
    if tokens[0].startswith("fit"):
        return None
    
    # Degenerate repetition check
    if len(tokens) >= 2 and len(set(tokens)) == 1:
        return None
    
    return s


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
            if label == "name":
                # Inline version of iter_source_lines with title stripping
                path_abs = os.path.abspath(path)
                h = hashlib.blake2b(path_abs.encode("utf-8"), digest_size=8).digest()
                path_hash = int.from_bytes(h, "little") & 0xFFFFFFFF
                rng = random.Random((seed & 0xFFFFFFFF) ^ path_hash)
                with io.open(path, "r", encoding="utf-8", errors="ignore") as f:
                    for raw in f:
                        if fraction < 1.0 and rng.random() >= fraction:
                            continue
                        cleaned = clean_name_line(raw)
                        s = normalize_line(cleaned) if cleaned else None
                        if s:
                            yield s
            else:
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


