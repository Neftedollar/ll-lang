#!/usr/bin/env bash
set -euo pipefail

# Demo path validated in AGE-233 for the working-demo post.
# Prerequisites:
# - `lllc` installed and available on PATH
# - .NET 10 SDK installed for `lllc self`
# - run from the ll-lang repo root

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

tmp_dir="$(mktemp -d)"
trap 'rm -rf "$tmp_dir"' EXIT

cat > "$tmp_dir/hello.lll" <<'EOF'
module Hello

add(a Int)(b Int) Int = a + b
greet(name Str) Str = strConcat "Hello, " name
factorial(n Int) Int =
  match n
    | 0 -> 1
    | _ -> n * factorial (n - 1)
EOF

echo '$ lllc --help'
lllc --help
echo

echo '$ cat > hello.lll'
cat "$tmp_dir/hello.lll"
echo

echo '$ lllc build --target ts hello.lll'
(cd "$tmp_dir" && lllc build --target ts hello.lll)
echo

echo '$ cat hello.ts'
sed -n '1,120p' "$tmp_dir/hello.ts"
echo

echo '$ lllc build --target py hello.lll'
(cd "$tmp_dir" && lllc build --target py hello.lll)
echo

echo '$ lllc self check lllcself/src/Main.lll'
lllc self check lllcself/src/Main.lll
