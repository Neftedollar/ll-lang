# ll-lang Token Efficiency Benchmark

*Generated: 2026-04-15*
*Tokenizer: cl100k_base (GPT-4)*

## Tier 1: Compiled Output Comparison

| Sample | ll-lang | F# (no prelude) | F# (w/prelude) | TS | Ratio (F#/lll) |
|--------|---------|-----------------|----------------|----|----------------|
| 01-basics | 110 | 122 | 718 | 142 | 1.11 |
| Map (RBTree) | 1570 | 1132 | 1132 | - | 0.72 |
| Toml parser | 1749 | 2359 | 3135 | - | 1.35 |
| Bootstrap | 16957 | 18393 | 19169 | - | 1.08 |

## Tier 2: Hand-written Equivalent

| Sample | ll-lang | F# (hand-written) | Ratio |
|--------|---------|-------------------|-------|
| TOML parser | 1749 | 1823 | 1.04 |

## Tier 3: Micro-benchmarks

| Pattern | lll | F# | TS | Py | Java | C# | F#/lll | TS/lll | Py/lll | Java/lll | C#/lll |
|---------|-----|----|----|-----|------|-----|--------|--------|--------|----------|--------|
| sum_type_3_ctor | 10 | 14 | 36 | 49 | 35 | 28 | 1.4 | 3.6 | 4.9 | 3.5 | 2.8 |
| pattern_match | 41 | 43 | 58 | 52 | 58 | 58 | 1.05 | 1.41 | 1.27 | 1.41 | 1.41 |
| curried_fn | 11 | 21 | 20 | 18 | 16 | 17 | 1.91 | 1.82 | 1.64 | 1.45 | 1.55 |
| parametric_adt | 7 | 12 | 23 | 47 | 31 | 24 | 1.71 | 3.29 | 6.71 | 4.43 | 3.43 |

## Per-file Details

| File | Lines | Code lines | Tokens (raw) | Tokens (code-only) | Bytes | Bytes/token |
|------|-------|-----------|-------------|-------------------|-------|-------------|
| 01-basics.lll | 28 | 16 | 136 | 110 | 379 | 3.4 |
| 01-basics.fs | 63 | 52 | 738 | 718 | 2359 | 3.3 |
| 01-basics.ts | 20 | 8 | 166 | 142 | 499 | 3.5 |
| Map.lll | 205 | 137 | 1988 | 1570 | 5902 | 3.8 |
| Map.fs | 54 | 37 | 1134 | 1132 | 3066 | 2.7 |
| Toml.lll | 266 | 194 | 2046 | 1749 | 7125 | 4.1 |
| Toml.fs | 131 | 103 | 3160 | 3135 | 9841 | 3.1 |
| 20-bootstrap-compiler.lll | 2987 | 1589 | 36055 | 16957 | 120209 | 7.1 |
| 20-bootstrap-compiler.fs | 1220 | 957 | 19759 | 19169 | 59163 | 3.1 |
| Manifest.fs | 227 | 188 | 2255 | 1823 | 9638 | 5.3 |

