#!/usr/bin/env python3
"""llvm-add-declares.py — post-process lllc's .ll output for clang/llc.

The ll-lang LLVM codegen (CodegenLLVM.fs, frozen) emits IR that `llvm-as` is
happy with but clang's built-in parser rejects in a few spots. This script
does three MVP fixups, then writes the patched IR to stdout:

  1. Prepend missing `declare` lines for every @name referenced by `call`
     but not defined or declared (lets clang link the runtime externals).
  2. Un-parenthesise instruction-form GEPs —
        %t0 = getelementptr inbounds ([N x i8], ptr @.s, i64 0, i64 0)
     becomes
        %t0 = getelementptr inbounds [N x i8], ptr @.s, i64 0, i64 0
     (the paren form is only legal as a constant-expression).
  3. Rewrite `define void @main()` -> `define i32 @main()` with `ret i32 0`
     so the native binary exits cleanly instead of returning stack garbage.

A small hardcoded table of known MVP runtime externals is used as the source
of truth for signatures. For unknown externals we fall back to parsing the
call site.

Usage: python3 llvm-add-declares.py <input.ll>   (writes patched IR to stdout)
"""

from __future__ import annotations

import re
import sys
from typing import Dict, List, Optional, Tuple

# Known runtime externals. Maps function name -> (return type, arg types).
# These are the MVP set matched against sdks/Platform.LLVM.SDK/runtime/lllc_runtime.c.
KNOWN_EXTERNALS: Dict[str, Tuple[str, List[str]]] = {
    # I/O
    "printfn":     ("void", ["ptr"]),
    "print_str":   ("void", ["ptr"]),
    "print_int":   ("void", ["i64"]),
    "console_log": ("void", ["ptr"]),
    "read_line":   ("ptr",  []),
    "read_file":   ("ptr",  ["ptr"]),
    "write_file":  ("void", ["ptr", "ptr"]),
    "readFile":    ("ptr",  ["ptr"]),
    "writeFile":   ("void", ["ptr", "ptr"]),
    # Strings (camelCase — matches what codegen actually emits)
    "strConcat":   ("ptr",  ["ptr", "ptr"]),
    "strLen":      ("i64",  ["ptr"]),
    "strEq":       ("i1",   ["ptr", "ptr"]),
    "intToStr":    ("ptr",  ["i64"]),
    # Strings (snake_case — matches Runtime.lll external declarations)
    "str_concat":  ("ptr",  ["ptr", "ptr"]),
    "str_len":     ("i64",  ["ptr"]),
    "str_eq":      ("i1",   ["ptr", "ptr"]),
    "int_to_str":  ("ptr",  ["i64"]),
    "str_from_int":("ptr",  ["i64"]),
    # libc
    "malloc":      ("ptr",  ["i64"]),
    "free":        ("void", ["ptr"]),
    "strcmp":      ("i32",  ["ptr", "ptr"]),
    # GC
    "gc_alloc":    ("i64",  ["i64"]),
    "gc_collect":  ("void", []),
    # ADT / lists
    "adt_alloc":   ("i64",  ["i64", "i64"]),
    "adt_tag":     ("i64",  ["i64"]),
    "adt_field":   ("i64",  ["i64", "i64"]),
    "list_nil":    ("i64",  []),
    "list_cons":   ("i64",  ["i64", "i64"]),
    "list_head":   ("i64",  ["i64"]),
    "list_tail":   ("i64",  ["i64"]),
    "list_is_empty": ("i1",  ["i64"]),
    # Codegen-internal allocator
    "__ll_alloc":  ("ptr",  ["i64", "i64", "ptr"]),
}


DEFINE_RE  = re.compile(r"^\s*define\s+\S+\s+@([A-Za-z0-9_.]+)\s*\(")
DECLARE_RE = re.compile(r"^\s*declare\s+\S+\s+@([A-Za-z0-9_.]+)\s*\(")
CALL_RE    = re.compile(r"call\s+(\S+)\s+@([A-Za-z0-9_.]+)\(([^)]*)\)")

# Instruction-form GEP with extraneous parens.
# Matches:  getelementptr inbounds (<type>, <args...>)
# Captures: group 1 = everything after `inbounds ` inside the parens.
INSTR_GEP_RE = re.compile(
    r"(?P<prefix>^\s*%[A-Za-z0-9_.]+\s*=\s*getelementptr\s+inbounds\s+)"
    r"\((?P<inner>.*)\)\s*$"
)

# `define void @main() {` -- the void-returning entry point that needs to
# become `i32` so the process exits 0 instead of with stack garbage.
VOID_MAIN_SIG_RE = re.compile(r"^(\s*)define\s+void\s+@main\s*\(\s*\)\s*\{\s*$")

# Codegen emits a local definition of @__ll_alloc (ADT allocator) that
# duplicates the implementation in lllc_runtime.c. When an example uses
# ADT pattern matching both symbols appear in the link, yielding
# `duplicate symbol '___ll_alloc'`. We keep the runtime as source of
# truth (so memory-management tweaks land in C, not in frozen codegen)
# and strip the generated definition textually.
RUNTIME_OWNED_DEFS = {"__ll_alloc"}
DEFINE_OWNED_RE = re.compile(
    r"^\s*define\s+\S+\s+@(" + "|".join(re.escape(n) for n in RUNTIME_OWNED_DEFS) + r")\s*\("
)


def parse_arg_types(args: str) -> List[str]:
    """Extract just the types from a call-site arg list like `ptr %t0, i64 3`."""
    args = args.strip()
    if not args:
        return []
    out = []
    for part in args.split(","):
        part = part.strip()
        if not part:
            continue
        # First whitespace-separated piece is the type.
        ty = part.split()[0]
        out.append(ty)
    return out


def collect_defined(lines: List[str]) -> set:
    """Names that already have a `define` or `declare`."""
    seen = set()
    for line in lines:
        m = DEFINE_RE.match(line) or DECLARE_RE.match(line)
        if m:
            seen.add(m.group(1))
    return seen


def collect_called(lines: List[str]) -> Dict[str, Tuple[str, List[str]]]:
    """First-seen signature for every name appearing in a `call` instruction."""
    called: Dict[str, Tuple[str, List[str]]] = {}
    for line in lines:
        for m in CALL_RE.finditer(line):
            ret_ty = m.group(1)
            name   = m.group(2)
            arg_ty = parse_arg_types(m.group(3))
            if name not in called:
                called[name] = (ret_ty, arg_ty)
    return called


def resolve_signature(
    name: str,
    observed: Optional[Tuple[str, List[str]]],
) -> Tuple[str, List[str]]:
    """Prefer the hardcoded MVP table; fall back to what we parsed from calls."""
    if name in KNOWN_EXTERNALS:
        return KNOWN_EXTERNALS[name]
    assert observed is not None, f"no signature info for {name}"
    return observed


def format_declare(name: str, ret_ty: str, arg_tys: List[str]) -> str:
    return f"declare {ret_ty} @{name}({', '.join(arg_tys)})"


def fix_instruction_geps(lines: List[str]) -> List[str]:
    """Strip the extra parens from `%t = getelementptr inbounds (<type>, ...)`.

    clang's IR parser only accepts the parenthesised form as a
    constant-expression. As an instruction the type and operands must NOT be
    wrapped. (llvm-as is more lenient; clang isn't.)
    """
    out = []
    for line in lines:
        m = INSTR_GEP_RE.match(line)
        if m is None:
            out.append(line)
            continue
        out.append(m.group("prefix") + m.group("inner"))
    return out


def fix_void_main(lines: List[str]) -> List[str]:
    """Convert `define void @main()` to `define i32 @main()` returning 0.

    Codegen emits `void` for `main() = <void-expr>`, but C runtime linkage
    expects main to return int — otherwise the process inherits whatever
    garbage was in the return register. Rewriting the signature + swapping
    `ret void` for `ret i32 0` is a purely textual fixup that keeps the body
    intact.
    """
    out: List[str] = []
    in_void_main = False
    brace_depth = 0
    for line in lines:
        if not in_void_main:
            m = VOID_MAIN_SIG_RE.match(line)
            if m is not None:
                indent = m.group(1)
                out.append(f"{indent}define i32 @main() {{")
                in_void_main = True
                brace_depth = 1
                continue
            out.append(line)
            continue

        # Inside the void-main body: rewrite ret void; track braces to know
        # when the function ends (nested braces in metadata are rare in
        # lllc output, but we still count to be safe).
        stripped = line.strip()
        if stripped == "ret void":
            indent_len = len(line) - len(line.lstrip())
            out.append(" " * indent_len + "ret i32 0")
        else:
            out.append(line)

        brace_depth += line.count("{") - line.count("}")
        if brace_depth <= 0:
            in_void_main = False
    return out


def prepend_declares(lines: List[str]) -> List[str]:
    """Insert `declare` lines for every undeclared @name referenced by `call`."""
    defined = collect_defined(lines)
    called  = collect_called(lines)

    missing = sorted(n for n in called.keys() if n not in defined)
    if not missing:
        return lines

    decls = [format_declare(n, *resolve_signature(n, called.get(n))) for n in missing]
    banner = (
        ["; --- auto-generated declares (llvm-add-declares.py) ---"]
        + decls
        + ["; --- end auto-generated declares ---", ""]
    )

    # Insert after any leading `; ...` banner comments / blank lines so the
    # file's own header stays on top.
    insert_at = 0
    for i, line in enumerate(lines):
        stripped = line.strip()
        if stripped == "" or stripped.startswith(";"):
            insert_at = i + 1
            continue
        break

    return lines[:insert_at] + banner + lines[insert_at:]


def strip_runtime_owned_defs(lines: List[str]) -> List[str]:
    """Drop `define ... @name(...) { ... }` blocks for functions the C runtime
    owns. Brace-counting keeps nested-brace safety (rare, but cheap).

    Without this, examples with ADT pattern matching fail linking with
    `duplicate symbol '___ll_alloc'` because both the IR-emitted and
    C-runtime versions end up in the object file.
    """
    out: List[str] = []
    i = 0
    while i < len(lines):
        line = lines[i]
        if DEFINE_OWNED_RE.match(line):
            # Skip until matching close brace.
            brace_depth = line.count("{") - line.count("}")
            i += 1
            while i < len(lines) and brace_depth > 0:
                brace_depth += lines[i].count("{") - lines[i].count("}")
                i += 1
            continue
        out.append(line)
        i += 1
    return out


LABEL_RE = re.compile(r"^([A-Za-z_][A-Za-z0-9_.]*):\s*$")
PHI_ENTRY_RE = re.compile(r"\[\s*([^,\]]+?)\s*,\s*%([A-Za-z0-9_.]+)\s*\]")
BR_RE = re.compile(r"^\s*br\s+(?:label\s+%([A-Za-z0-9_.]+)|i1\s+\S+\s*,\s*label\s+%([A-Za-z0-9_.]+)\s*,\s*label\s+%([A-Za-z0-9_.]+))")


def fix_match_phi_predecessors(lines: List[str]) -> List[str]:
    """Repair stale match_end phi predecessor labels.

    When a match-arm body contains nested control flow (e.g. if/else),
    codegen emits an `if_end_N` block that branches to `match_end_M`, but
    the phi at `match_end_M` still references `match_body_K` as the
    predecessor. We fix each stale entry by searching forward from the
    original body label to the actual block that branches to the phi's
    containing block along the arm's control path.

    The repair is scoped per function (bounded by `define ... {` /
    closing `}`). A block "reaches" match_end along path P if a DFS
    from the body label reaches a `br label %match_end_M` without
    leaving the function; we pick the direct-predecessor block found.
    """
    out: List[str] = []
    i = 0
    n = len(lines)
    while i < n:
        line = lines[i]
        # Find function start.
        if DEFINE_RE.match(line):
            # Collect function body until matching close brace.
            body_start = i
            brace = line.count("{") - line.count("}")
            j = i + 1
            while j < n and brace > 0:
                brace += lines[j].count("{") - lines[j].count("}")
                j += 1
            body_end = j  # exclusive
            func_lines = lines[body_start:body_end]
            func_lines = _repair_phis_in_func(func_lines)
            out.extend(func_lines)
            i = body_end
            continue
        out.append(line)
        i += 1
    return out


def _repair_phis_in_func(flines: List[str]) -> List[str]:
    # Parse labels -> line-range, and collect terminators.
    # A "block" is the sequence of lines from label+1 until the next label or `}`.
    label_positions: List[Tuple[int, str]] = []  # (idx, name)
    for idx, ln in enumerate(flines):
        m = LABEL_RE.match(ln.rstrip())
        if m:
            label_positions.append((idx, m.group(1)))
    # Determine block ranges: block i is (label_positions[i].idx + 1, next_label_idx_or_end)
    blocks: Dict[str, Tuple[int, int]] = {}
    for k, (idx, name) in enumerate(label_positions):
        start = idx + 1
        end = label_positions[k + 1][0] if k + 1 < len(label_positions) else len(flines)
        blocks[name] = (start, end)

    # Compute terminator successors for each block.
    succs: Dict[str, List[str]] = {name: [] for name in blocks}
    for name, (s, e) in blocks.items():
        for li in range(e - 1, s - 1, -1):
            ln = flines[li]
            bm = BR_RE.match(ln)
            if bm:
                if bm.group(1) is not None:
                    succs[name] = [bm.group(1)]
                else:
                    succs[name] = [bm.group(2), bm.group(3)]
                break

    # Build predecessors.
    preds: Dict[str, List[str]] = {name: [] for name in blocks}
    for src, sl in succs.items():
        for dst in sl:
            if dst in preds:
                preds[dst].append(src)

    # For each phi, check entries; repair stale ones.
    for block_name, (s, e) in blocks.items():
        block_preds = set(preds.get(block_name, []))
        for li in range(s, e):
            ln = flines[li]
            if "phi " not in ln:
                continue
            entries = PHI_ENTRY_RE.findall(ln)
            if not entries:
                continue
            new_ln = ln
            entry_labels = [lab for (_, lab) in entries]
            # For each stale entry, attempt to find a replacement: a real
            # predecessor reachable from the stale label via forward DFS.
            for idx_entry, (val, lab) in enumerate(entries):
                if lab in block_preds:
                    continue
                # Find predecessors reachable from `lab`.
                reachable_real_preds: List[str] = []
                visited = set()
                stack = [lab]
                while stack:
                    cur = stack.pop()
                    if cur in visited:
                        continue
                    visited.add(cur)
                    if cur in block_preds and cur not in entry_labels:
                        reachable_real_preds.append(cur)
                        continue
                    for nxt in succs.get(cur, []):
                        if nxt == block_name:
                            # The immediate predecessor of this path is `cur`.
                            if cur in block_preds and cur not in entry_labels:
                                reachable_real_preds.append(cur)
                        else:
                            stack.append(nxt)
                if len(reachable_real_preds) == 1:
                    replacement = reachable_real_preds[0]
                    # Replace the specific entry `[val, %lab]` with `[val, %replacement]`.
                    old = f"[ {val}, %{lab} ]"
                    new = f"[ {val}, %{replacement} ]"
                    # Because bracket spacing may vary, do a safer regex-based swap.
                    pattern = re.compile(
                        r"\[\s*" + re.escape(val) + r"\s*,\s*%" + re.escape(lab) + r"\s*\]"
                    )
                    new_ln = pattern.sub(f"[ {val}, %{replacement} ]", new_ln, count=1)
            if new_ln != ln:
                flines[li] = new_ln
    return flines


def patch(text: str) -> str:
    lines = text.splitlines()
    lines = strip_runtime_owned_defs(lines)
    lines = fix_instruction_geps(lines)
    lines = fix_void_main(lines)
    lines = fix_match_phi_predecessors(lines)
    lines = prepend_declares(lines)
    result = "\n".join(lines)
    if text.endswith("\n"):
        result += "\n"
    return result


def main(argv: List[str]) -> int:
    if len(argv) != 2:
        print("usage: llvm-add-declares.py <input.ll>", file=sys.stderr)
        return 2
    with open(argv[1], "r", encoding="utf-8") as f:
        src = f.read()
    sys.stdout.write(patch(src))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
