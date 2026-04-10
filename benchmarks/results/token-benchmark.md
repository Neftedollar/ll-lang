# ll-lang Token Efficiency Benchmark

*Generated: 2026-04-10*
*Tokenizer: cl100k_base (GPT-4)*

## Tier 1: Compiled Output Comparison

| Sample | ll-lang | F# (no prelude) | F# (w/prelude) | TS | Ratio (F#/lll) |
|--------|---------|-----------------|----------------|----|----------------|
| 01-basics | 116 | 118 | 714 | 142 | 1.02 |
| Map (RBTree) | 1897 | 2073 | 2849 | - | 1.09 |
| Toml parser | 2170 | 2359 | 3135 | - | 1.09 |
| Bootstrap | 17822 | 18372 | 19148 | - | 1.03 |

## Tier 2: Hand-written Equivalent

| Sample | ll-lang | F# (hand-written) | Ratio |
|--------|---------|-------------------|-------|
| TOML parser | 2170 | 1439 | 0.66 |

## Tier 3: Micro-benchmarks

| Pattern | lll | F# | TS | Py | Java | F#/lll | TS/lll | Py/lll | Java/lll |
|---------|-----|----|----|-----|------|--------|--------|--------|----------|
| sum_type_3_ctor | 11 | 14 | 36 | 49 | 35 | 1.27 | 3.27 | 4.45 | 3.18 |
| pattern_match | 39 | 43 | 58 | 52 | 58 | 1.1 | 1.49 | 1.33 | 1.49 |
| curried_fn | 13 | 21 | 20 | 18 | 16 | 1.62 | 1.54 | 1.38 | 1.23 |
| parametric_adt | 8 | 12 | 23 | 47 | 31 | 1.5 | 2.88 | 5.88 | 3.88 |

## Per-file Details

| File | Lines | Code lines | Tokens (raw) | Tokens (code-only) | Bytes | Bytes/token |
|------|-------|-----------|-------------|-------------------|-------|-------------|
| 01-basics.lll | 26 | 14 | 142 | 116 | 408 | 3.5 |
| 01-basics.fs | 60 | 49 | 734 | 714 | 2343 | 3.3 |
| 01-basics.ts | 20 | 8 | 166 | 142 | 499 | 3.5 |
| Map.lll | 223 | 152 | 2320 | 1897 | 6874 | 3.6 |
| Map.fs | 109 | 88 | 2871 | 2849 | 8049 | 2.8 |
| Toml.lll | 272 | 194 | 2519 | 2170 | 8594 | 4.0 |
| Toml.fs | 131 | 103 | 3160 | 3135 | 9841 | 3.1 |
| 20-bootstrap-compiler.lll | 2869 | 1468 | 36960 | 17822 | 123621 | 6.9 |
| 20-bootstrap-compiler.fs | 1223 | 960 | 19739 | 19148 | 59258 | 3.1 |
| Manifest.fs | 182 | 154 | 1729 | 1439 | 7816 | 5.4 |

