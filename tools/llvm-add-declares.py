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


def patch(text: str) -> str:
    lines = text.splitlines()
    lines = strip_runtime_owned_defs(lines)
    lines = fix_instruction_geps(lines)
    lines = fix_void_main(lines)
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
