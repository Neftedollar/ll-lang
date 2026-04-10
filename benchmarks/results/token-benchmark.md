# ll-lang Token Efficiency Benchmark

*Generated: 2026-04-10*
*Tokenizer: cl100k_base (GPT-4)*

## Tier 1: Compiled Output Comparison

| Sample | ll-lang | F# (no prelude) | F# (w/prelude) | TS | Ratio (F#/lll) |
|--------|---------|-----------------|----------------|----|----------------|
| 01-basics | 118 | 118 | 714 | 142 | 1.0 |
| Map (RBTree) | 1910 | 2073 | 2849 | - | 1.09 |
| Toml parser | 2175 | 2359 | 3135 | - | 1.08 |
| Bootstrap | 17918 | 18372 | 19148 | - | 1.03 |

## Tier 2: Hand-written Equivalent

| Sample | ll-lang | F# (hand-written) | Ratio |
|--------|---------|-------------------|-------|
| TOML parser | 2175 | 1439 | 0.66 |

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
| 01-basics.lll | 28 | 16 | 144 | 118 | 406 | 3.4 |
| 01-basics.fs | 60 | 49 | 734 | 714 | 2343 | 3.3 |
| 01-basics.ts | 20 | 8 | 166 | 142 | 499 | 3.5 |
| Map.lll | 235 | 164 | 2333 | 1910 | 6879 | 3.6 |
| Map.fs | 109 | 88 | 2871 | 2849 | 8049 | 2.8 |
| Toml.lll | 292 | 214 | 2524 | 2175 | 8580 | 3.9 |
| Toml.fs | 131 | 103 | 3160 | 3135 | 9841 | 3.1 |
| 20-bootstrap-compiler.lll | 2997 | 1596 | 37054 | 17918 | 123685 | 6.9 |
| 20-bootstrap-compiler.fs | 1223 | 960 | 19739 | 19148 | 59258 | 3.1 |
| Manifest.fs | 182 | 154 | 1729 | 1439 | 7816 | 5.4 |

