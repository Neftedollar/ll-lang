# ll-lang Token Efficiency Benchmark

*Generated: 2026-04-11*
*Tokenizer: cl100k_base (GPT-4)*

## Tier 1: Compiled Output Comparison

| Sample | ll-lang | F# (no prelude) | F# (w/prelude) | TS | Ratio (F#/lll) |
|--------|---------|-----------------|----------------|----|----------------|
| 01-basics | 110 | 122 | 718 | 142 | 1.11 |
| Map (RBTree) | 1771 | 2073 | 2849 | - | 1.17 |
| Toml parser | 2030 | 2359 | 3135 | - | 1.16 |
| Bootstrap | 16957 | 18393 | 19169 | - | 1.08 |

## Tier 2: Hand-written Equivalent

| Sample | ll-lang | F# (hand-written) | Ratio |
|--------|---------|-------------------|-------|
| TOML parser | 2030 | 1823 | 0.9 |

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
| 01-basics.lll | 28 | 16 | 136 | 110 | 379 | 3.4 |
| 01-basics.fs | 63 | 52 | 738 | 718 | 2359 | 3.3 |
| 01-basics.ts | 20 | 8 | 166 | 142 | 499 | 3.5 |
| Map.lll | 235 | 164 | 2194 | 1771 | 6393 | 3.6 |
| Map.fs | 109 | 88 | 2871 | 2849 | 8049 | 2.8 |
| Toml.lll | 292 | 214 | 2379 | 2030 | 8002 | 3.9 |
| Toml.fs | 131 | 103 | 3160 | 3135 | 9841 | 3.1 |
| 20-bootstrap-compiler.lll | 2987 | 1589 | 36055 | 16957 | 120209 | 7.1 |
| 20-bootstrap-compiler.fs | 1220 | 957 | 19759 | 19169 | 59163 | 3.1 |
| Manifest.fs | 227 | 188 | 2255 | 1823 | 9638 | 5.3 |

