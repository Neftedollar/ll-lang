---
title: ll-lang
hide:
  - toc
---

<div class="ll-home">
  <section class="ll-hero">
    <div class="ll-hero__copy">
      <img
        class="ll-wordmark"
        src="assets/brand/ll-lang-wordmark-light.svg"
        alt="ll-lang wordmark"
      />
      <p class="ll-kicker">Statically-typed functional language for LLM code generation</p>
      <h1>Give language models less syntax to waste.</h1>
      <p class="ll-lead">
        ll-lang keeps prompts compact, catches mistakes before execution, and
        returns diagnostics an agent can repair without parsing human prose.
      </p>
      <div class="ll-actions">
        <a class="md-button md-button--primary" href="https://github.com/Neftedollar/ll-lang">Open GitHub</a>
        <a class="md-button" href="https://dev.to/neftedollar/the-2600-line-compiler-that-compiles-itself-and-emits-f-typescript-python-java-and-c-49lh">See it in action</a>
        <a class="md-button" href="why-ll-lang/">Why ll-lang</a>
        <a class="md-button" href="language-spec/">Read the spec</a>
        <a class="md-button" href="https://github.com/Neftedollar/ll-lang/discussions">Leave beta feedback →</a>
      </div>
      <div class="ll-stats">
        <div class="ll-stat">
          <span class="ll-stat__value">8–17%</span>
          <span class="ll-stat__label">smaller than F# on measured code</span>
        </div>
        <div class="ll-stat">
          <span class="ll-stat__value">1.3–5.9x</span>
          <span class="ll-stat__label">more compact than TS, Python, and Java on type-heavy samples</span>
        </div>
        <div class="ll-stat">
          <span class="ll-stat__value">30</span>
          <span class="ll-stat__label">MCP tools exposed by <code>lllc mcp</code></span>
        </div>
      </div>
    </div>
    <div class="ll-hero__media">
      <img
        class="ll-hero__gif"
        src="assets/hero/hero-story.gif"
        alt="Animated side-by-side comparison of TypeScript and ll-lang with token counters"
      />
      <p class="ll-caption">
        Same benchmark sample, side by side: 142 TypeScript tokens versus 110 ll-lang tokens.
      </p>
    </div>
  </section>

  <section class="ll-command-bar">
    <span class="ll-command-bar__label">Install a pinned bootstrap compiler</span>
    <code>./tools/bootstrap-self.sh install</code>
  </section>

  <section class="ll-section ll-section--contrast">
    <div>
      <div class="ll-section__eyebrow">See it in action</div>
      <h2>A full self-host cycle now has a working demo.</h2>
      <p>
        The latest walkthrough shows one compact ll-lang source file compiling to
        multiple targets, then the compiler checking its own source in the
        self-hosted path.
      </p>
      <div class="ll-link-row">
        <a href="https://dev.to/neftedollar/the-2600-line-compiler-that-compiles-itself-and-emits-f-typescript-python-java-and-c-49lh">Read the dev.to post</a>
        <a href="https://github.com/Neftedollar/ll-lang/blob/main/tools/demo-self-host.sh">Open demo script</a>
      </div>
    </div>
    <div class="ll-hero__media">
      <img
        class="ll-hero__gif"
        src="assets/demo/self-host-cycle.gif"
        alt="Animated terminal demo of bootstrap install, multi-target compile, and ll-lang self-host check"
      />
      <p class="ll-caption">
        Roughly 24 seconds: install, compile to TypeScript and Python, then run the self-host check.
      </p>
    </div>
  </section>

  <section class="ll-section">
    <div class="ll-section__eyebrow">Why ll-lang</div>
    <h2>Built for the feedback loop LLMs actually live in.</h2>
    <div class="ll-grid ll-grid--three">
      <article class="ll-card">
        <h3>Smaller prompts, same logic</h3>
        <p>
          Less punctuation and ceremony means more of the context window goes to
          intent, types, and behavior.
        </p>
      </article>
      <article class="ll-card">
        <h3>Compile before you execute</h3>
        <p>
          Hindley-Milner inference, tagged values, and exhaustive matches move
          bugs into the compiler instead of production logs.
        </p>
      </article>
      <article class="ll-card">
        <h3>Diagnostics models can repair</h3>
        <p>
          Error codes stay single-line and structured, so agents can fix one
          concrete issue at a time.
        </p>
      </article>
    </div>
  </section>

  <section class="ll-section ll-section--contrast">
    <div>
      <div class="ll-section__eyebrow">Live snippet</div>
      <h2>The “hello world” path stays short.</h2>
      <p>
        The language surface is compact enough for quick prompting, but it still
        compiles to real targets and ships with a built-in MCP server.
      </p>
      <div class="ll-link-row">
        <a href="https://github.com/Neftedollar/ll-lang/blob/main/README.md">README</a>
        <a href="language-spec/">Spec</a>
        <a href="playground/">Playground status</a>
      </div>
    </div>
    <div class="ll-code-stack">
      <pre><code class="language-lll">module Hello

Hello = printfn "Hello, ll-lang!"</code></pre>
      <pre><code class="language-bash">./tools/lllc-bootstrap.sh run hello.lll
# Hello, ll-lang!</code></pre>
    </div>
  </section>

  <section class="ll-section">
    <div class="ll-section__eyebrow">Proof points</div>
    <h2>Not a toy syntax demo.</h2>
    <div class="ll-grid ll-grid--two">
      <article class="ll-proof">
        <h3>Self-hosting compiler</h3>
        <p>
          ll-lang compiles itself. The bootstrap compiler reaches a fixpoint:
          <code>compiler₁.fs == compiler₂.fs</code>.
        </p>
      </article>
      <article class="ll-proof">
        <h3>Multi-target output</h3>
        <p>
          One source can emit F#, TypeScript, Python, Java, C#, and an
          experimental LLVM backend.
        </p>
      </article>
      <article class="ll-proof">
        <h3>Real tooling surface</h3>
        <p>
          <code>lllc mcp</code> exposes compile, diagnose, symbol, fix-preview,
          and project graph tools for editor agents.
        </p>
      </article>
      <article class="ll-proof">
        <h3>Docs stay close to the metal</h3>
        <p>
          The landing page pushes deeper reading into the README, user guide,
          spec, and compiler internals instead of duplicating them.
        </p>
      </article>
    </div>
  </section>
</div>
