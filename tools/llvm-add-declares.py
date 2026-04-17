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
# When ll_getArgs is referenced post-rewrite, we add a declare for it here.
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
    # CLI arg support (see fix_cli_args_main below)
    "ll_getArgs":  ("ptr",  []),
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

# `define <ret> @main() {` -- the user entry point. We rename it to
# `@ll_main` so the C runtime's `int main(int, char**)` can wrap it
# (capturing argv) and return a real i32 exit code. Previously we rewrote
# this to `define i32 @main()` + `ret i32 0`, but that precluded CLI args.
#
# Frozen codegen emits either `void @main` (for statement-shaped entries
# like `printfn "..."`) or a value-returning `<type> @main` (e.g. `i32`
# / `i64` when main's body evaluates to a scalar — see example 21 where
# `rbSize m` produces `i32 @main`). In the latter case we coerce the
# signature to void and rewrite any `ret <type> <val>` to `ret void`;
# the user-level return value is discarded because the OS exit code comes
# from the C runtime's `int main` wrapper.
MAIN_SIG_RE = re.compile(r"^(\s*)define\s+(\S+)\s+@main\s*\(\s*\)\s*\{\s*$")

# `call void @printArgs(ptr null)` — the pattern emitted by the frozen
# codegen when the user writes `main() = printArgs []`. We intercept it
# and rewrite so the null becomes the real argv list built at runtime.
# Matching by explicit "ptr null" keeps the rewrite targeted; callers that
# legitimately pass `null` would need a different convention.
CLI_ARGS_CALL_RE = re.compile(
    r"(?P<indent>\s*)call\s+void\s+@printArgs\s*\(\s*ptr\s+null\s*\)\s*$"
)

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


def rename_main_to_ll_main(lines: List[str]) -> List[str]:
    """Rename `define <ret> @main()` to `define void @ll_main()`.

    The C runtime (lllc_runtime.c) defines the real `int main(int, char**)`
    which captures argv and delegates to `ll_main`. We coerce the signature
    to `void` even when the frozen codegen emits a value-returning main
    (e.g. `i32`/`i64` when the body evaluates to a scalar) — the user-level
    return value is discarded because the OS exit code comes from the C
    wrapper. Any `ret <type> <val>` inside the body is rewritten to
    `ret void` in that case.

    We also rewrite any self-recursive `call ... @main(` inside the
    renamed body (unusual but possible). Calls to *other* functions named
    `main` in user code are disambiguated by lllc's module suffix, so this
    only fires on the synthesised entry.
    """
    out: List[str] = []
    in_main = False
    ret_ty: Optional[str] = None
    brace_depth = 0
    for line in lines:
        if not in_main:
            m = MAIN_SIG_RE.match(line)
            if m is not None:
                indent = m.group(1)
                ret_ty = m.group(2)
                out.append(f"{indent}define void @ll_main() {{")
                in_main = True
                brace_depth = 1
                continue
            out.append(line)
            continue

        # Inside the renamed main.
        rewritten = line
        # If main was value-returning, coerce every `ret <ty> <val>` to `ret void`.
        if ret_ty is not None and ret_ty != "void":
            ret_pattern = re.compile(r"^(\s*)ret\s+\S+\s+\S+\s*$")
            if ret_pattern.match(rewritten):
                indent = ret_pattern.match(rewritten).group(1)
                rewritten = f"{indent}ret void"
        # Rewrite self-recursive calls to the old name.
        rewritten = re.sub(r"call\s+(\S+)\s+@main\(", r"call \1 @ll_main(", rewritten)
        out.append(rewritten)

        brace_depth += line.count("{") - line.count("}")
        if brace_depth <= 0:
            in_main = False
            ret_ty = None
    return out


def fix_cli_args_main(lines: List[str]) -> List[str]:
    """Wire `main() = printArgs []` up to real command-line arguments.

    Frozen codegen emits literal `[]` as `ptr null`, which means the
    pattern `call void @printArgs(ptr null)` at the entry point is our
    signal that the user wants CLI args. We rewrite that one call site to:

        %__cli_args = call ptr @ll_getArgs()
        call void @printArgs(ptr %__cli_args)

    `@ll_getArgs` is implemented in lllc_runtime.c — it reads the argc/argv
    captured by the real `int main(...)` and materialises a cons list of
    strings. The declare for `@ll_getArgs` is added automatically by
    prepend_declares via KNOWN_EXTERNALS.

    This workaround is scoped tightly (`@printArgs(ptr null)`, only inside
    `@ll_main`) because the frozen codegen drops references to value-shaped
    prelude identifiers like `getArgs` — we need a call that codegen *will*
    emit, then retrofit the argument.
    """
    out: List[str] = []
    in_ll_main = False
    brace_depth = 0
    for line in lines:
        if not in_ll_main:
            if re.match(r"^\s*define\s+void\s+@ll_main\s*\(\s*\)\s*\{\s*$", line):
                in_ll_main = True
                brace_depth = 1
            out.append(line)
            continue

        m = CLI_ARGS_CALL_RE.match(line)
        if m is not None:
            indent = m.group("indent")
            out.append(f"{indent}%__cli_args = call ptr @ll_getArgs()")
            out.append(f"{indent}call void @printArgs(ptr %__cli_args)")
        else:
            out.append(line)

        brace_depth += line.count("{") - line.count("}")
        if brace_depth <= 0:
            in_ll_main = False
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


MATCH_BODY_RE       = re.compile(r"^match_body_\d+:\s*$")
GEP_LITSTR_RE       = re.compile(
    r"^\s*%(?P<name>t\d+)\s*=\s*getelementptr\s+inbounds\s+"
    r"(?:\()?\[\s*\d+\s*x\s*i8\s*\],\s*ptr\s+@\.str\d+\s*,\s*i64\s+0\s*,\s*i64\s+0\)?\s*$"
)
INTTOPTR_RE         = re.compile(
    r"^\s*%(?P<name>t\d+)\s*=\s*inttoptr\s+i64\s+%t\d+\s+to\s+ptr\s*$"
)
BR_MATCH_END_RE     = re.compile(r"^(?P<indent>\s*)br\s+label\s+%(?P<lbl>match_end_\d+)\s*$")


def fix_match_arm_str_concat(lines: List[str]) -> List[str]:
    """Synthesise `strConcat` inside match arms whose body operand sequence
    looks like "literal string + payload cast to ptr" — a pattern the frozen
    codegen emits without the actual concat call (phi entry ends up `null`).

    Detection (per `match_body_N` block, function-scoped):

      match_body_N:
        %tA = getelementptr inbounds ([K x i8], ptr @.strX, i64 0, i64 0)
        %tB = inttoptr i64 %tY to ptr
        br label %match_end_M                                   ; last instr

      match_end_M:
        %tZ = phi ptr [ null, %match_body_N ], ...

    Rewrite: insert `%cat_N = call ptr @strConcat(ptr %tA, ptr %tB)` before
    the branch, and change the phi entry `[ null, %match_body_N ]` to
    `[ %cat_N, %match_body_N ]`. The declare for `@strConcat` is added
    automatically by `prepend_declares`.

    Scoped narrowly (exactly: one literal GEP + one inttoptr, a null phi
    entry, a ptr phi) so non-matching arms are untouched. This compensates
    for a frozen-codegen drop in match arms that evaluate `"lit" + payload`,
    e.g. example 10's `TIdent s -> "id:" + s` arm.
    """
    out_lines = list(lines)

    # Index label -> line number, scope-bounded by function braces.
    labels: Dict[str, int] = {}
    i = 0
    while i < len(out_lines):
        ln = out_lines[i]
        m = LABEL_RE.match(ln.rstrip())
        if m:
            labels[m.group(1)] = i
        i += 1

    # Snapshot only the match_body_* labels (by name); look up positions
    # in the live `labels` dict each iteration so inserts stay consistent.
    body_labels = [n for n in labels if re.match(r"^match_body_\d+$", n)]
    for lbl in body_labels:
        body_line = labels.get(lbl)
        if body_line is None:
            continue
        if not re.match(r"^match_body_\d+:\s*$", out_lines[body_line]):
            continue

        # Collect the block until the next label or close brace.
        gep_name: Optional[str] = None
        intptr_name: Optional[str] = None
        br_line_idx: Optional[int] = None
        match_end_lbl: Optional[str] = None
        j = body_line + 1
        stray = False
        while j < len(out_lines):
            ln = out_lines[j]
            if LABEL_RE.match(ln.rstrip()) or ln.strip() == "}":
                break
            gm = GEP_LITSTR_RE.match(ln)
            if gm:
                if gep_name is None:
                    gep_name = gm.group("name")
                else:
                    # More than one literal-GEP in the arm — bail.
                    stray = True
                j += 1
                continue
            im = INTTOPTR_RE.match(ln)
            if im:
                if intptr_name is None:
                    intptr_name = im.group("name")
                else:
                    stray = True
                j += 1
                continue
            bm = BR_MATCH_END_RE.match(ln)
            if bm:
                br_line_idx = j
                match_end_lbl = bm.group("lbl")
                break
            # Any other instruction in the block disqualifies the rewrite.
            # (We only care about the "1 literal + 1 payload + br" shape.)
            stray = True
            j += 1

        if stray or gep_name is None or intptr_name is None or br_line_idx is None or match_end_lbl is None:
            continue

        # The phi at match_end_lbl must have a `null` entry for this body.
        phi_idx = labels.get(match_end_lbl)
        if phi_idx is None:
            continue
        phi_line_idx: Optional[int] = None
        for k in range(phi_idx + 1, len(out_lines)):
            l2 = out_lines[k]
            if LABEL_RE.match(l2.rstrip()) or l2.strip() == "}":
                break
            if "phi ptr " in l2 and f"%{lbl}" in l2:
                phi_line_idx = k
                break
        if phi_line_idx is None:
            continue

        phi_line = out_lines[phi_line_idx]
        null_entry_re = re.compile(r"\[\s*null\s*,\s*%" + re.escape(lbl) + r"\s*\]")
        if not null_entry_re.search(phi_line):
            continue

        # All clear — synthesise the strConcat.
        cat_name = f"cat_{lbl}"
        br_line = out_lines[br_line_idx]
        indent_m = re.match(r"^(\s*)", br_line)
        indent = indent_m.group(1) if indent_m else "  "
        call_line = f"{indent}%{cat_name} = call ptr @strConcat(ptr %{gep_name}, ptr %{intptr_name})"

        # Replace the phi's null entry with %cat_name.
        new_phi_line = null_entry_re.sub(f"[ %{cat_name}, %{lbl} ]", phi_line, count=1)

        # Apply edits in reverse order so earlier indices remain valid.
        out_lines[phi_line_idx] = new_phi_line
        out_lines.insert(br_line_idx, call_line)

        # Re-index labels since we've inserted a line.
        for name in list(labels.keys()):
            if labels[name] > br_line_idx:
                labels[name] += 1

    return out_lines


MODULE_MARKER_RE = re.compile(r"^;\s*Module:\s*(?P<name>\S+)\s*$")
STR_GLOBAL_DEF_RE = re.compile(r"^@\.str\d+\s*=\s")


def uniquify_module_private_strings(lines: List[str]) -> List[str]:
    """Rename per-module `@.strN` globals so they don't collide across modules.

    The frozen LLVM codegen emits `@.str0`, `@.str1`, ... inside each module
    as `private unnamed_addr constant`s for string literals. When lllc
    concatenates many modules into a single `.ll` (which happens for any
    file that imports stdlib — and especially for the self-hosted compiler
    `lllcself`, which pulls in ~20 modules), these names collide and clang
    emits `redefinition of global '@.strN'`.

    Because `private` globals are module-local in LLVM's linkage model,
    renaming them has no cross-module semantic effect. We parse the IR
    as a sequence of module sections delimited by `; Module: <Name>`
    comments (which the codegen emits as section headers), and within each
    section rewrite `@.strN` -> `@.str_<Module>_N` in both definitions
    and references. We leave the first section (before any `; Module:`
    marker) as-is; numbering restarts in each module so earlier sections
    stay valid on their own.

    Uses of `@.strN` outside any module section (e.g. in the final
    section after the last module marker) are left untouched. Cross-module
    references to `@.strN` are impossible in well-formed IR because these
    symbols have private linkage.
    """
    # 1. Partition lines into sections by `; Module: <name>` markers.
    sections: List[Tuple[Optional[str], int, int]] = []  # (module, start, end)
    current_module: Optional[str] = None
    section_start = 0
    for i, line in enumerate(lines):
        m = MODULE_MARKER_RE.match(line)
        if m:
            # Close previous section [section_start, i)
            sections.append((current_module, section_start, i))
            current_module = m.group("name")
            section_start = i
    # Close the trailing section.
    sections.append((current_module, section_start, len(lines)))

    # 2. For each module section, find its local `@.strN` definitions,
    #    then rewrite those exact names in defs + all uses within the
    #    same section. Sanitise the module name for use as an LLVM
    #    identifier (dots -> underscores).
    def sanitise(name: str) -> str:
        return re.sub(r"[^A-Za-z0-9_]", "_", name)

    # Work on a mutable copy.
    out = list(lines)
    # Track the highest `@.strN` seen so far so the rename target never
    # collides with some later module's `@.strK`. We use the scheme
    # `@.str_<Module>_N` which includes the module name, so collisions
    # require two modules with the same sanitised name (unlikely).
    for module_name, start, end in sections:
        if module_name is None:
            continue  # Leave header section as-is.
        # Find `@.strN` defined in this section.
        local_names: List[str] = []
        for i in range(start, end):
            ln = out[i]
            if STR_GLOBAL_DEF_RE.match(ln):
                # Extract the full name up to ` = `.
                name = ln.split(" ", 1)[0]  # "@.strN"
                local_names.append(name)
        if not local_names:
            continue
        mod_tag = sanitise(module_name)
        # Build a renaming map: "@.strN" -> "@.str_<Mod>_<N>".
        # Sort by descending length so "@.str10" is replaced before "@.str1".
        rename_map: Dict[str, str] = {}
        for name in local_names:
            num = name[len("@.str"):]
            rename_map[name] = f"@.str_{mod_tag}_{num}"
        # Rewrite the section. Use word-boundary-ish regex to avoid
        # accidentally hitting `@.str10` when replacing `@.str1`.
        # We compile one combined pattern sorted by descending length.
        sorted_names = sorted(rename_map.keys(), key=len, reverse=True)
        # Escape for regex; match only when followed by a non-digit / non-word char.
        pattern = re.compile(
            r"(" + "|".join(re.escape(n) for n in sorted_names) + r")(?![0-9A-Za-z_])"
        )
        for i in range(start, end):
            ln = out[i]
            if "@.str" not in ln:
                continue
            out[i] = pattern.sub(lambda m: rename_map[m.group(1)], ln)
    return out


DECLARE_LINE_RE = re.compile(r"^\s*declare\s+")


def dedupe_declares(lines: List[str]) -> List[str]:
    """Drop duplicate `declare` lines for the same name.

    When many modules are concatenated into a single `.ll` (as happens
    for any file that imports stdlib — see `uniquify_module_private_strings`),
    each module independently emits `declare ptr @malloc(i64)` (and
    similar externals). LLVM's textual IR considers `declare` lines
    redundant if they agree — but clang's parser rejects a second
    `declare` for a name as `invalid redefinition of function`.

    We keep the first `declare` for each name and drop later ones,
    regardless of whether the signatures agree. (If signatures diverge
    the first wins and any later mis-use would fail type-check; in
    practice the codegen emits the same external signature every time.)
    """
    seen: set = set()
    out: List[str] = []
    for line in lines:
        m = DECLARE_RE.match(line)
        if m:
            name = m.group(1)
            if name in seen:
                continue
            seen.add(name)
        out.append(line)
    return out


def patch(text: str) -> str:
    lines = text.splitlines()
    lines = strip_runtime_owned_defs(lines)
    lines = uniquify_module_private_strings(lines)
    lines = dedupe_declares(lines)
    lines = fix_instruction_geps(lines)
    lines = rename_main_to_ll_main(lines)
    lines = fix_cli_args_main(lines)
    lines = fix_match_phi_predecessors(lines)
    lines = fix_match_arm_str_concat(lines)
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
