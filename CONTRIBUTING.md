# Contributing to ll-lang

Thank you for your interest in contributing! This guide covers everything you need to get started.

## Getting the code running

No .NET installation required for the default bootstrap path.

```bash
git clone https://github.com/Neftedollar/ll-lang.git
cd ll-lang
./tools/lllc-bootstrap.sh check spec/examples/valid/01-basics.lll
```

This downloads the pinned bootstrap binary (SHA-256 verified against `bootstrap/lllc-bootstrap.lock.json`) and runs a quick compile check. If that produces `{"ok":true,...}` you are ready to go.

Full CI suite (takes a few minutes):

```bash
./tools/check-selfhost-ci.sh
```

## Adding a corpus example

Corpus examples live in `spec/examples/valid/`. A minimal new example:

1. Create `spec/examples/valid/NN-my-feature.lll` following the numbering convention.
2. Start with `module MyFeature` and keep it under ~80 lines.
3. Run `./tools/lllc-bootstrap.sh check spec/examples/valid/NN-my-feature.lll` — output should be `{"ok":true,...}`.
4. If the example exercises codegen, add a matching entry in `tools/check-selfhost-e2e.sh`.

## Filing a bug

Please include:

- **ll-lang version** — `./tools/lllc-bootstrap.sh --version`
- **Operating system and shell**
- **Minimal reproducer** — the shortest `.lll` file that triggers the problem
- **Expected vs actual output** — paste the raw `{"ok":false,...}` JSON or error text

Open the issue at https://github.com/Neftedollar/ll-lang/issues.

## Language spec and grammar

- Language spec: [`docs/language-spec.md`](docs/language-spec.md)
- Getting started guide: [`docs/getting-started.md`](docs/getting-started.md)
- Stdlib reference: [`docs/stdlib-reference.md`](docs/stdlib-reference.md)
- Error codes: `./tools/lllc-bootstrap.sh list_errors`

The formal grammar lives in `src/` (parser source). When adding a syntax feature, update `docs/language-spec.md` alongside the implementation.

## How PRs are reviewed

- All CI checks must pass (see `.github/workflows/build.yml`).
- Self-host CI (`tools/check-selfhost-ci.sh`) and E2E (`tools/check-selfhost-e2e.sh`) are the primary gates.
- Keep PRs focused — one logical change per PR makes review faster.
- New language features need a corpus example in `spec/examples/valid/`.
- Bug fixes should add a regression test fixture or corpus entry.
- Reviewer turnaround is generally within a few days.

## Code of conduct

This project follows the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md). By participating you agree to abide by its terms.
