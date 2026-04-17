---
name: Bug report
about: Something doesn't compile, crashes, or produces wrong output
title: ''
labels: bug
assignees: ''
---

**Minimal .lll file that reproduces the bug**

```lll
module Repro

-- paste the smallest file that triggers the bug
```

**Command you ran**

```bash
lllc build hello.lll
# or: lllc run --target ts hello.lll
```

**Expected output / behaviour**

**Actual output / error**

```
paste lllc output here
```

**Version**

```bash
lllc --version
# or: dotnet tool list -g | grep lllc
```

**Platform**: npm / pip / dotnet tool / from source
