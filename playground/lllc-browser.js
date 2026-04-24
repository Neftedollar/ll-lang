// ll-lang browser playground compiler
// Implements tokenize → parse → TypeScript emit for single-module programs.
// Follows the ll-lang grammar spec and CodegenTS.lll emitter semantics.

/* global LllcBrowser */
const LllcBrowser = (() => {
  'use strict';

  // ─── TOKENIZER ──────────────────────────────────────────────────────────────

  const T = {
    INT: 'INT', FLOAT: 'FLOAT', STR: 'STR', CHAR: 'CHAR',
    IDENT: 'IDENT', TYPEID: 'TYPEID',
    // keywords
    KW_MODULE: 'KW_MODULE', KW_IMPORT: 'KW_IMPORT', KW_EXPORT: 'KW_EXPORT',
    KW_LET: 'KW_LET', KW_MATCH: 'KW_MATCH', KW_IF: 'KW_IF', KW_ELSE: 'KW_ELSE',
    KW_TRUE: 'KW_TRUE', KW_FALSE: 'KW_FALSE',
    KW_EXTERNAL: 'KW_EXTERNAL', KW_OPAQUE: 'KW_OPAQUE',
    KW_TAG: 'KW_TAG', KW_UNIT: 'KW_UNIT',
    KW_TRAIT: 'KW_TRAIT', KW_IMPL: 'KW_IMPL',
    KW_INFIX: 'KW_INFIX', KW_INFIXL: 'KW_INFIXL', KW_INFIXR: 'KW_INFIXR',
    // punctuation / operators
    EQ: 'EQ', LPAREN: 'LPAREN', RPAREN: 'RPAREN',
    LBRACK: 'LBRACK', RBRACK: 'RBRACK',
    COMMA: 'COMMA', DOT: 'DOT', COLON: 'COLON',
    ARROW: 'ARROW', PIPE_FWD: 'PIPE_FWD',
    BAR: 'BAR', CONS: 'CONS',
    LAMBDA: 'LAMBDA', UNDERSCORE: 'UNDERSCORE',
    EQEQ: 'EQEQ', NEQ: 'NEQ', LT: 'LT', GT: 'GT', LE: 'LE', GE: 'GE',
    PLUS: 'PLUS', MINUS: 'MINUS', STAR: 'STAR', SLASH: 'SLASH',
    CARET: 'CARET', PERCENT: 'PERCENT', AMPAMP: 'AMPAMP', BARBAR: 'BARBAR',
    BIND: 'BIND', SEQ: 'SEQ', CHOICE: 'CHOICE',
    // layout
    NEWLINE: 'NEWLINE', INDENT: 'INDENT', DEDENT: 'DEDENT',
    EOF: 'EOF',
  };

  const KEYWORDS = {
    module: T.KW_MODULE, import: T.KW_IMPORT, export: T.KW_EXPORT,
    let: T.KW_LET, match: T.KW_MATCH, if: T.KW_IF, else: T.KW_ELSE,
    true: T.KW_TRUE, false: T.KW_FALSE,
    external: T.KW_EXTERNAL, opaque: T.KW_OPAQUE,
    tag: T.KW_TAG, unit: T.KW_UNIT,
    trait: T.KW_TRAIT, impl: T.KW_IMPL,
    infix: T.KW_INFIX, infixl: T.KW_INFIXL, infixr: T.KW_INFIXR,
  };

  function tokenize(src) {
    const lines = src.split('\n');
    const tokens = [];
    const indentStack = [0];

    function tok(type, value, line, col) {
      return { type, value, line, col };
    }

    function lexLine(raw, lineNum) {
      // Measure indentation
      let col = 0;
      while (col < raw.length && (raw[col] === ' ' || raw[col] === '\t')) col++;
      const indent = col;
      const content = raw.slice(col);

      // Skip blank lines and comment-only lines
      if (content === '' || content.startsWith('--')) return null;

      return { indent, content, lineNum, rawIndentCol: col };
    }

    let i = 0;
    while (i < lines.length) {
      const info = lexLine(lines[i], i + 1);
      i++;
      if (!info) continue;

      const { indent, content, lineNum } = info;
      const top = indentStack[indentStack.length - 1];

      if (indent > top) {
        indentStack.push(indent);
        tokens.push(tok(T.INDENT, indent, lineNum, 1));
      } else if (indent < top) {
        while (indentStack.length > 1 && indentStack[indentStack.length - 1] > indent) {
          indentStack.pop();
          tokens.push(tok(T.DEDENT, indent, lineNum, 1));
        }
        tokens.push(tok(T.NEWLINE, '\n', lineNum, 1));
      } else if (tokens.length > 0) {
        tokens.push(tok(T.NEWLINE, '\n', lineNum, 1));
      }

      // Lex content of this line
      let j = 0;
      function peek(k) { return content[j + (k || 0)] || ''; }
      function advance() { return content[j++]; }

      while (j < content.length) {
        // skip whitespace
        if (content[j] === ' ' || content[j] === '\t') { j++; continue; }
        // comment
        if (content[j] === '-' && content[j + 1] === '-') break;

        const startCol = j + 1;
        const ch = content[j];

        // String literal
        if (ch === '"') {
          j++;
          let s = '';
          while (j < content.length && content[j] !== '"') {
            if (content[j] === '\\') {
              j++;
              const esc = content[j] || '';
              s += esc === 'n' ? '\n' : esc === 't' ? '\t' : esc === 'r' ? '\r' : esc;
            } else {
              s += content[j];
            }
            j++;
          }
          j++; // closing "
          tokens.push(tok(T.STR, s, lineNum, startCol));
          continue;
        }

        // Char literal
        if (ch === "'") {
          j++;
          let c = '';
          if (content[j] === '\\') {
            j++;
            const esc = content[j] || '';
            c = esc === 'n' ? '\n' : esc === 't' ? '\t' : esc === 'r' ? '\r' : esc;
          } else {
            c = content[j] || '';
          }
          j++;
          j++; // closing '
          tokens.push(tok(T.CHAR, c, lineNum, startCol));
          continue;
        }

        // Number
        if (ch >= '0' && ch <= '9') {
          let num = '';
          while (j < content.length && content[j] >= '0' && content[j] <= '9') num += content[j++];
          if (content[j] === '.' && content[j + 1] >= '0' && content[j + 1] <= '9') {
            num += content[j++];
            while (j < content.length && content[j] >= '0' && content[j] <= '9') num += content[j++];
            tokens.push(tok(T.FLOAT, parseFloat(num), lineNum, startCol));
          } else {
            tokens.push(tok(T.INT, parseInt(num, 10), lineNum, startCol));
          }
          continue;
        }

        // Identifier / keyword
        if ((ch >= 'a' && ch <= 'z') || ch === '_') {
          let id = '';
          while (j < content.length && /[\w]/.test(content[j])) id += content[j++];
          const kw = KEYWORDS[id];
          tokens.push(tok(kw || T.IDENT, id, lineNum, startCol));
          continue;
        }

        // Type identifier
        if (ch >= 'A' && ch <= 'Z') {
          let id = '';
          while (j < content.length && /[\w]/.test(content[j])) id += content[j++];
          tokens.push(tok(T.TYPEID, id, lineNum, startCol));
          continue;
        }

        // Two-char operators
        const two = content.slice(j, j + 2);
        if (two === '->') { tokens.push(tok(T.ARROW, '->', lineNum, startCol)); j += 2; continue; }
        if (two === '|>') { tokens.push(tok(T.PIPE_FWD, '|>', lineNum, startCol)); j += 2; continue; }
        if (two === '::') { tokens.push(tok(T.CONS, '::', lineNum, startCol)); j += 2; continue; }
        if (two === '==') { tokens.push(tok(T.EQEQ, '==', lineNum, startCol)); j += 2; continue; }
        if (two === '!=') { tokens.push(tok(T.NEQ, '!=', lineNum, startCol)); j += 2; continue; }
        if (two === '<=') { tokens.push(tok(T.LE, '<=', lineNum, startCol)); j += 2; continue; }
        if (two === '>=') { tokens.push(tok(T.GE, '>=', lineNum, startCol)); j += 2; continue; }
        if (two === '>>=') { tokens.push(tok(T.BIND, '>>=', lineNum, startCol)); j += 3; continue; }
        if (two === '>>') { tokens.push(tok(T.SEQ, '>>', lineNum, startCol)); j += 2; continue; }
        if (two === '<|>') { tokens.push(tok(T.CHOICE, '<|>', lineNum, startCol)); j += 3; continue; }
        if (two === '&&') { tokens.push(tok(T.AMPAMP, '&&', lineNum, startCol)); j += 2; continue; }
        if (two === '||') { tokens.push(tok(T.BARBAR, '||', lineNum, startCol)); j += 2; continue; }

        // Single-char operators
        j++;
        switch (ch) {
          case '=': tokens.push(tok(T.EQ, '=', lineNum, startCol)); break;
          case '(': tokens.push(tok(T.LPAREN, '(', lineNum, startCol)); break;
          case ')': tokens.push(tok(T.RPAREN, ')', lineNum, startCol)); break;
          case '[': tokens.push(tok(T.LBRACK, '[', lineNum, startCol)); break;
          case ']': tokens.push(tok(T.RBRACK, ']', lineNum, startCol)); break;
          case ',': tokens.push(tok(T.COMMA, ',', lineNum, startCol)); break;
          case '.': tokens.push(tok(T.DOT, '.', lineNum, startCol)); break;
          case ':': tokens.push(tok(T.COLON, ':', lineNum, startCol)); break;
          case '|': tokens.push(tok(T.BAR, '|', lineNum, startCol)); break;
          case '\\': tokens.push(tok(T.LAMBDA, '\\', lineNum, startCol)); break;
          case '_': tokens.push(tok(T.UNDERSCORE, '_', lineNum, startCol)); break;
          case '<': tokens.push(tok(T.LT, '<', lineNum, startCol)); break;
          case '>': tokens.push(tok(T.GT, '>', lineNum, startCol)); break;
          case '+': tokens.push(tok(T.PLUS, '+', lineNum, startCol)); break;
          case '-': tokens.push(tok(T.MINUS, '-', lineNum, startCol)); break;
          case '*': tokens.push(tok(T.STAR, '*', lineNum, startCol)); break;
          case '/': tokens.push(tok(T.SLASH, '/', lineNum, startCol)); break;
          case '^': tokens.push(tok(T.CARET, '^', lineNum, startCol)); break;
          case '%': tokens.push(tok(T.PERCENT, '%', lineNum, startCol)); break;
          default:
            throw mkError(`Unexpected character '${ch}'`, lineNum, startCol);
        }
      }
    }

    // Close any remaining indentation levels
    while (indentStack.length > 1) {
      indentStack.pop();
      tokens.push(tok(T.DEDENT, 0, lines.length, 1));
    }
    tokens.push(tok(T.EOF, null, lines.length, 1));
    return tokens;
  }

  // ─── PARSER ─────────────────────────────────────────────────────────────────

  function mkError(msg, line, col) {
    const e = new Error(`E001 ${line}:${col}: ${msg}`);
    e.lllLine = line; e.lllCol = col;
    return e;
  }

  function Parser(tokens) {
    let pos = 0;
    function peek(k) { return tokens[pos + (k || 0)] || { type: T.EOF, value: null }; }
    function cur() { return tokens[pos]; }
    function advance() { return tokens[pos++]; }
    function at(type) { return cur().type === type; }
    function atAny(...types) { return types.includes(cur().type); }

    function skipNewlines() {
      while (atAny(T.NEWLINE, T.INDENT, T.DEDENT)) advance();
    }

    function expect(type, msg) {
      if (!at(type)) {
        const t = cur();
        throw mkError(msg || `Expected ${type} but got ${t.type} ('${t.value}')`, t.line, t.col);
      }
      return advance();
    }

    // Parse a block: a sequence of items delimited by INDENT/DEDENT (or a single item inline)
    function parseBlock(parseItem) {
      const items = [];
      if (at(T.INDENT)) {
        advance(); // consume INDENT
        while (!atAny(T.DEDENT, T.EOF)) {
          skipNewlines();
          if (atAny(T.DEDENT, T.EOF)) break;
          items.push(parseItem());
          skipNewlines();
        }
        if (at(T.DEDENT)) advance();
      } else {
        // inline single item (same line)
        items.push(parseItem());
      }
      return items;
    }

    // ── Type expressions ──
    function parseTypeExpr() {
      let base = parseTypeAtom();
      // TyFn: `a -> b`
      while (at(T.ARROW)) {
        advance();
        const rhs = parseTypeAtom();
        base = { kind: 'TyFn', a: base, b: rhs };
      }
      return base;
    }

    function parseTypeAtom() {
      if (at(T.TYPEID)) {
        const name = advance().value;
        // Type application: if next is another type atom, it's applied
        if (at(T.TYPEID) || at(T.LBRACK)) {
          const arg = parseTypeAtom();
          return { kind: 'TyApp', f: { kind: 'TyName', name }, a: arg };
        }
        return { kind: 'TyName', name };
      }
      if (at(T.LBRACK)) {
        advance();
        const inner = parseTypeExpr();
        expect(T.RBRACK, "Expected ']' to close list type");
        return { kind: 'TyApp', f: { kind: 'TyName', name: 'List' }, a: inner };
      }
      if (at(T.LPAREN)) {
        advance();
        const inner = parseTypeExpr();
        expect(T.RPAREN);
        return inner;
      }
      const t = cur();
      throw mkError(`Expected type expression but got '${t.value}'`, t.line, t.col);
    }

    // ── Pattern ──
    function parsePattern() {
      if (at(T.IDENT)) {
        const name = advance().value;
        return { kind: 'PVar', name };
      }
      if (at(T.UNDERSCORE)) { advance(); return { kind: 'PWild' }; }
      if (at(T.INT)) { return { kind: 'PLitInt', n: advance().value }; }
      if (at(T.STR)) { return { kind: 'PLitStr', s: advance().value }; }
      if (at(T.KW_TRUE)) { advance(); return { kind: 'PLitBool', b: true }; }
      if (at(T.KW_FALSE)) { advance(); return { kind: 'PLitBool', b: false }; }
      if (at(T.LBRACK)) {
        advance();
        if (at(T.RBRACK)) { advance(); return { kind: 'PNil' }; }
        expect(T.RBRACK);
      }
      if (at(T.TYPEID)) {
        const name = advance().value;
        const args = [];
        while (!atAny(T.NEWLINE, T.DEDENT, T.EOF, T.BAR, T.ARROW)) {
          args.push(parsePatternAtom());
        }
        return { kind: 'PCon', name, args };
      }
      if (at(T.LPAREN)) {
        advance();
        if (at(T.RPAREN)) { advance(); return { kind: 'PUnit' }; }
        const inner = parsePattern();
        // Cons pattern inside parens
        if (at(T.CONS)) {
          advance();
          const tail = parsePattern();
          expect(T.RPAREN);
          return { kind: 'PCons', head: inner, tail };
        }
        expect(T.RPAREN);
        return inner;
      }
      const t = cur();
      throw mkError(`Expected pattern but got '${t.value}'`, t.line, t.col);
    }

    function parsePatternAtom() {
      if (at(T.IDENT)) return { kind: 'PVar', name: advance().value };
      if (at(T.UNDERSCORE)) { advance(); return { kind: 'PWild' }; }
      if (at(T.INT)) return { kind: 'PLitInt', n: advance().value };
      if (at(T.STR)) return { kind: 'PLitStr', s: advance().value };
      if (at(T.KW_TRUE)) { advance(); return { kind: 'PLitBool', b: true }; }
      if (at(T.KW_FALSE)) { advance(); return { kind: 'PLitBool', b: false }; }
      if (at(T.LBRACK)) {
        advance();
        if (at(T.RBRACK)) { advance(); return { kind: 'PNil' }; }
        expect(T.RBRACK);
      }
      if (at(T.LPAREN)) {
        advance();
        if (at(T.RPAREN)) { advance(); return { kind: 'PUnit' }; }
        const inner = parsePattern();
        if (at(T.CONS)) {
          advance();
          const tail = parsePattern();
          expect(T.RPAREN);
          return { kind: 'PCons', head: inner, tail };
        }
        expect(T.RPAREN);
        return inner;
      }
      if (at(T.TYPEID)) {
        const name = advance().value;
        return { kind: 'PCon', name, args: [] };
      }
      const t = cur();
      throw mkError(`Expected pattern atom but got '${t.value}'`, t.line, t.col);
    }

    // ── Expressions ──
    // Precedence (loosest to tightest):
    // 1: <|>  2: >>= >>  3: -> |>  4: || &&  5: == != < > <= >=  6: ::  7: + -  8: * / %  9: app

    function parseExpr() { return parsePipeExpr(); }

    function parsePipeExpr() {
      let lhs = parseOrExpr();
      while (atAny(T.ARROW, T.PIPE_FWD)) {
        const op = advance().value;
        const rhs = parseOrExpr();
        lhs = { kind: 'EBinOp', op: '->', l: lhs, r: rhs };
      }
      return lhs;
    }

    function parseOrExpr() {
      let lhs = parseAndExpr();
      while (at(T.BARBAR)) {
        advance();
        const rhs = parseAndExpr();
        lhs = { kind: 'EBinOp', op: '||', l: lhs, r: rhs };
      }
      return lhs;
    }

    function parseAndExpr() {
      let lhs = parseCmpExpr();
      while (at(T.AMPAMP)) {
        advance();
        const rhs = parseCmpExpr();
        lhs = { kind: 'EBinOp', op: '&&', l: lhs, r: rhs };
      }
      return lhs;
    }

    function parseCmpExpr() {
      let lhs = parseConsExpr();
      while (atAny(T.EQEQ, T.NEQ, T.LT, T.GT, T.LE, T.GE)) {
        const op = advance().value;
        const rhs = parseConsExpr();
        lhs = { kind: 'EBinOp', op, l: lhs, r: rhs };
      }
      return lhs;
    }

    function parseConsExpr() {
      const lhs = parseAddExpr();
      if (at(T.CONS)) {
        advance();
        const rhs = parseConsExpr(); // right-associative
        return { kind: 'ECons', head: lhs, tail: rhs };
      }
      return lhs;
    }

    function parseAddExpr() {
      let lhs = parseMulExpr();
      while (atAny(T.PLUS, T.MINUS)) {
        const op = advance().value;
        const rhs = parseMulExpr();
        lhs = { kind: 'EBinOp', op, l: lhs, r: rhs };
      }
      return lhs;
    }

    function parseMulExpr() {
      let lhs = parseUnaryExpr();
      while (atAny(T.STAR, T.SLASH, T.PERCENT)) {
        const op = advance().value;
        const rhs = parseUnaryExpr();
        lhs = { kind: 'EBinOp', op, l: lhs, r: rhs };
      }
      return lhs;
    }

    function parseUnaryExpr() {
      if (at(T.MINUS)) {
        const t = advance();
        const e = parseAppExpr();
        return { kind: 'EBinOp', op: '-', l: { kind: 'EInt', n: 0 }, r: e };
      }
      return parseAppExpr();
    }

    function parseAppExpr() {
      let f = parseAtom();
      // Function application: greedy left-associative
      while (isAtomStart()) {
        const arg = parseAtom();
        f = { kind: 'EApp', f, arg };
      }
      return f;
    }

    function isAtomStart() {
      return atAny(
        T.INT, T.FLOAT, T.STR, T.CHAR, T.KW_TRUE, T.KW_FALSE,
        T.IDENT, T.TYPEID, T.LPAREN, T.LBRACK
      );
    }

    function parseAtom() {
      if (at(T.INT)) { return { kind: 'EInt', n: advance().value }; }
      if (at(T.FLOAT)) { return { kind: 'EFloat', n: advance().value }; }
      if (at(T.STR)) { return { kind: 'EStr', s: advance().value }; }
      if (at(T.CHAR)) { return { kind: 'EChar', c: advance().value }; }
      if (at(T.KW_TRUE)) { advance(); return { kind: 'EBool', b: true }; }
      if (at(T.KW_FALSE)) { advance(); return { kind: 'EBool', b: false }; }

      if (at(T.IDENT)) {
        const t = advance();
        // Let-binding expression: `x = e` inside a function body
        // We detect it when the IDENT is followed by `=` (not `==`) at expression level
        if (at(T.EQ)) {
          advance();
          const val = parseExpr();
          skipNewlines();
          const body = parseExpr();
          return { kind: 'ELet', name: t.value, val, body };
        }
        return { kind: 'EVar', name: t.value };
      }

      if (at(T.TYPEID)) {
        return { kind: 'ECon', name: advance().value };
      }

      if (at(T.LBRACK)) {
        advance();
        if (at(T.RBRACK)) { advance(); return { kind: 'ENil' }; }
        const items = [parseExpr()];
        while (at(T.COMMA)) { advance(); items.push(parseExpr()); }
        expect(T.RBRACK, "Expected ']' to close list literal");
        return { kind: 'EList', items };
      }

      if (at(T.LPAREN)) {
        advance();
        if (at(T.RPAREN)) { advance(); return { kind: 'EUnit' }; }
        const inner = parseExpr();
        // Tuple: (a, b)
        if (at(T.COMMA)) {
          advance();
          const b = parseExpr();
          expect(T.RPAREN);
          return { kind: 'ETuple', a: inner, b };
        }
        expect(T.RPAREN, "Expected ')'");
        return inner;
      }

      if (at(T.LAMBDA)) {
        advance();
        let param = '';
        if (at(T.IDENT)) param = advance().value;
        else if (at(T.UNDERSCORE)) { advance(); param = '_'; }
        else {
          const t = cur();
          throw mkError(`Expected parameter name after \\`, t.line, t.col);
        }
        expect(T.DOT, "Expected '.' after lambda parameter");
        const body = parseExpr();
        return { kind: 'ELam', param, body };
      }

      if (at(T.KW_IF)) {
        return parseIf();
      }

      if (at(T.KW_MATCH)) {
        return parseMatch();
      }

      if (at(T.KW_LET)) {
        advance();
        const name = expect(T.IDENT, "Expected identifier after 'let'").value;
        expect(T.EQ, "Expected '=' after let name");
        const val = parseExpr();
        skipNewlines();
        const body = parseExpr();
        return { kind: 'ELet', name, val, body };
      }

      const t = cur();
      throw mkError(`Unexpected token in expression: '${t.value}' (${t.type})`, t.line, t.col);
    }

    function parseIf() {
      expect(T.KW_IF);
      const cond = parseExpr();
      skipNewlines();
      // Then branch: indented block or same-line expression
      let thenBr;
      if (at(T.INDENT)) {
        advance();
        skipNewlines();
        thenBr = parseExpr();
        skipNewlines();
        if (at(T.DEDENT)) advance();
      } else {
        thenBr = parseExpr();
      }
      skipNewlines();
      // Else branch
      let elseBr = null;
      if (at(T.KW_ELSE)) {
        advance();
        skipNewlines();
        if (at(T.INDENT)) {
          advance();
          skipNewlines();
          elseBr = parseExpr();
          skipNewlines();
          if (at(T.DEDENT)) advance();
        } else {
          elseBr = parseExpr();
        }
      }
      return { kind: 'EIf', cond, thenBr, elseBr };
    }

    function parseMatch() {
      expect(T.KW_MATCH);
      const scrut = parseExpr();
      skipNewlines();
      const arms = [];
      // Match arms: | pattern -> body (or indented body)
      if (at(T.INDENT)) advance();
      while (at(T.BAR)) {
        advance();
        const pat = parsePattern();
        // Cons pattern with ::
        let finalPat = pat;
        if (at(T.CONS)) {
          advance();
          const tail = parsePattern();
          finalPat = { kind: 'PCons', head: pat, tail };
        }
        expect(T.ARROW, "Expected '->' after match pattern");
        skipNewlines();
        let body;
        if (at(T.INDENT)) {
          advance();
          skipNewlines();
          body = parseExpr();
          skipNewlines();
          if (at(T.DEDENT)) advance();
        } else {
          body = parseExpr();
        }
        arms.push({ pat: finalPat, body });
        skipNewlines();
      }
      if (at(T.DEDENT)) advance();
      return { kind: 'EMatch', scrut, arms };
    }

    // ── Declarations ──
    function parseParam() {
      expect(T.LPAREN, "Expected '(' for parameter");
      const name = expect(T.IDENT, "Expected parameter name").value;
      const type = parseTypeExpr();
      expect(T.RPAREN, "Expected ')' to close parameter");
      return { name, type };
    }

    function parseDecl() {
      // Import: `import Std.Map`
      if (at(T.KW_IMPORT)) {
        advance();
        const parts = [expect(T.TYPEID, "Expected module name after import").value];
        while (at(T.DOT)) {
          advance();
          parts.push(expect(T.TYPEID, "Expected module name after '.'").value);
        }
        return { kind: 'DImport', parts };
      }

      // Export: `export NAME = ...`
      if (at(T.KW_EXPORT)) {
        advance();
        const inner = parseDecl();
        return { kind: 'DExport', inner };
      }

      // Opaque type
      if (at(T.KW_OPAQUE)) {
        advance();
        const name = expect(T.TYPEID, "Expected type name after opaque").value;
        return { kind: 'DOpaque', name };
      }

      // External declaration
      if (at(T.KW_EXTERNAL)) {
        advance();
        const name = expect(T.IDENT, "Expected function name after external").value;
        const params = [];
        while (at(T.LPAREN)) params.push(parseParam());
        const retType = parseTypeExpr();
        return { kind: 'DExternal', name, params, retType };
      }

      // Type declaration: `TypeName = Ctor1 | Ctor2 ...`  or  `TypeName(tvars) = ...`
      if (at(T.TYPEID)) {
        const nameToken = cur();
        const name = advance().value;

        // Check if it's a type param list
        const tvars = [];
        while (at(T.IDENT) && peek(1).type !== T.EQ) {
          tvars.push(advance().value);
        }

        expect(T.EQ, `Expected '=' after type name '${name}'`);
        skipNewlines();

        // Constructor declarations
        const ctors = [];
        // Constructors can start on same line or in an indented block
        let inBlock = false;
        if (at(T.INDENT)) { advance(); inBlock = true; }

        const parseOneCtor = () => {
          expect(T.BAR, "Expected '|' for constructor");
          const ctorName = expect(T.TYPEID, "Expected constructor name").value;
          const args = [];
          while (!atAny(T.BAR, T.NEWLINE, T.DEDENT, T.EOF)) {
            args.push(parseTypeAtom());
          }
          return { name: ctorName, args };
        };

        if (at(T.BAR)) {
          ctors.push(parseOneCtor());
          skipNewlines();
          while (at(T.BAR)) {
            ctors.push(parseOneCtor());
            skipNewlines();
          }
        } else if (at(T.TYPEID)) {
          // Single constructor without `|`
          const ctorName = advance().value;
          const args = [];
          while (!atAny(T.NEWLINE, T.DEDENT, T.EOF, T.BAR)) {
            args.push(parseTypeAtom());
          }
          ctors.push({ name: ctorName, args });
          skipNewlines();
          while (at(T.BAR)) {
            ctors.push(parseOneCtor());
            skipNewlines();
          }
        }

        if (inBlock && at(T.DEDENT)) advance();
        return { kind: 'DType', name, tvars, ctors };
      }

      // Value / function declaration: `name(params...) = body` or `let name = body` or `name = body`
      if (at(T.KW_LET)) {
        advance();
        const name = expect(T.IDENT, "Expected identifier after 'let'").value;
        const params = [];
        while (at(T.LPAREN)) params.push(parseParam());
        expect(T.EQ, `Expected '=' in let declaration`);
        skipNewlines();
        const body = parseFnBody();
        return { kind: 'DFn', name, params, retType: null, body };
      }

      if (at(T.IDENT)) {
        const name = advance().value;
        const params = [];
        while (at(T.LPAREN)) params.push(parseParam());
        // Optional return type annotation: `-> Type`
        let retType = null;
        if (at(T.ARROW)) { advance(); retType = parseTypeExpr(); }
        expect(T.EQ, `Expected '=' in function declaration '${name}'`);
        skipNewlines();
        const body = parseFnBody();
        return { kind: 'DFn', name, params, retType, body };
      }

      // Fixity declaration — skip
      if (atAny(T.KW_INFIX, T.KW_INFIXL, T.KW_INFIXR)) {
        advance();
        while (!atAny(T.NEWLINE, T.DEDENT, T.EOF)) advance();
        return null;
      }

      const t = cur();
      throw mkError(`Unexpected token at top-level: '${t.value}' (${t.type})`, t.line, t.col);
    }

    function parseFnBody() {
      // A function body can be a multi-statement block (indented let chain) or a single expression
      if (at(T.INDENT)) {
        advance();
        const stmts = [];
        skipNewlines();
        while (!atAny(T.DEDENT, T.EOF)) {
          stmts.push(parseExpr());
          skipNewlines();
        }
        if (at(T.DEDENT)) advance();
        // Multi-statement body: last statement is the return value
        // Sequential let bindings: x = e1; y = e2; result
        // We build them as nested ELet if they look like assignments
        if (stmts.length === 0) throw new Error('Empty function body');
        if (stmts.length === 1) return stmts[0];
        // Fold: [s0, s1, ...sN] where s0..sN-1 should be ELet nodes
        // The parser already handles this in parseAtom via `IDENT = expr` detection
        // Just return the last expression with prior lets already nested
        return stmts[stmts.length - 1];
      }
      return parseExpr();
    }

    // ── Module ──
    function parseModule() {
      skipNewlines();
      expect(T.KW_MODULE, "Expected 'module' keyword at start of file");
      const path = [expect(T.TYPEID, "Expected module name").value];
      while (at(T.DOT)) {
        advance();
        path.push(expect(T.TYPEID, "Expected module name part after '.'").value);
      }
      skipNewlines();

      const decls = [];
      while (!at(T.EOF)) {
        skipNewlines();
        if (at(T.EOF)) break;
        const d = parseDecl();
        if (d) decls.push(d);
        skipNewlines();
      }
      return { kind: 'Module', path, decls };
    }

    return { parseModule };
  }

  // ─── TYPESCRIPT EMITTER ─────────────────────────────────────────────────────
  // Follows CodegenTS.lll semantics closely.

  const TS_KEYWORDS = new Set([
    'break','const','delete','else','false','for','function','if','in','new',
    'null','return','switch','this','true','try','typeof','var','void','while',
    'with','class','enum','extends','super','implements','interface','package',
    'private','protected','public','static','abstract','as','from','of','type',
    'let','import','export','async','await','declare','readonly','namespace',
    'module','require','undefined',
  ]);

  function safeIdent(s) {
    return TS_KEYWORDS.has(s) ? `__ll_${s}` : s;
  }

  function escapeStr(s) {
    return s
      .replace(/\\/g, '\\\\')
      .replace(/`/g, '\\`')
      .replace(/\$/g, '\\$')
      .replace(/\n/g, '\\n')
      .replace(/\t/g, '\\t')
      .replace(/\r/g, '\\r');
  }

  const OP_MAP = { '&&': '&&', '||': '||', '==': '===', '!=': '!==',
    '<': '<', '>': '>', '<=': '<=', '>=': '>=',
    '+': '+', '-': '-', '*': '*', '/': '/', '%': '%', '^': '**' };

  function emitType(t) {
    if (!t) return 'any';
    switch (t.kind) {
      case 'TyName': {
        const m = { Int: 'number', Float: 'number', Str: 'string', Bool: 'boolean',
          Char: 'string', Unit: 'void' };
        return m[t.name] || t.name;
      }
      case 'TyApp':
        if (t.f.kind === 'TyName' && t.f.name === 'List') return `${emitType(t.a)}[]`;
        if (t.f.kind === 'TyName' && t.f.name === 'Maybe') return `${emitType(t.a)} | null`;
        return `${emitType(t.f)}<${emitType(t.a)}>`;
      case 'TyFn':
        return `(_: ${emitType(t.a)}) => ${emitType(t.b)}`;
      default: return 'any';
    }
  }

  function emitPattern(p) {
    switch (p.kind) {
      case 'PVar': return safeIdent(p.name);
      case 'PWild': return '_';
      case 'PNil': return '[]';
      case 'PUnit': return '[]';
      case 'PLitInt': return String(p.n);
      case 'PLitStr': return '`' + escapeStr(p.s) + '`';
      case 'PLitBool': return p.b ? 'true' : 'false';
      case 'PCon': {
        if (p.args.length === 0) return p.name;
        return `${p.name}(${p.args.map(emitPattern).join(', ')})`;
      }
      case 'PCons': return `${emitPattern(p.head)}, ...${emitPattern(p.tail)}`;
      default: return '_';
    }
  }

  function emitExpr(e) {
    switch (e.kind) {
      case 'EInt': return String(e.n);
      case 'EFloat': return String(e.n);
      case 'EStr': return '`' + escapeStr(e.s) + '`';
      case 'EChar': return '`' + escapeStr(e.c) + '`';
      case 'EBool': return e.b ? 'true' : 'false';
      case 'EUnit': return 'undefined';
      case 'ENil': return '[]';
      case 'EVar': return safeIdent(e.name);
      case 'ECon': return safeIdent(e.name);

      case 'EBinOp': {
        // Pipe operator: (e |> f) = f(e)
        if (e.op === '->' || e.op === '|>') {
          return `(${emitExpr(e.r)})(${emitExpr(e.l)})`;
        }
        const op = OP_MAP[e.op] || e.op;
        return `(${emitExpr(e.l)} ${op} ${emitExpr(e.r)})`;
      }

      case 'ECons':
        return `[${emitExpr(e.head)}, ...${emitExpr(e.tail)}]`;

      case 'EApp': {
        // Collect all args for curried application
        const { head, args } = gatherApp(e);
        const headStr = emitExpr(head);
        return args.reduce((acc, arg) => `(${acc})(${emitExpr(arg)})`, headStr);
      }

      case 'ELam':
        return `(${safeIdent(e.param)}) => ${emitExpr(e.body)}`;

      case 'ELet':
        return `((${safeIdent(e.name)}) => ${emitExpr(e.body)})(${emitExpr(e.val)})`;

      case 'EIf': {
        const elsePart = e.elseBr ? emitExpr(e.elseBr) : 'undefined';
        return `(${emitExpr(e.cond)} ? ${emitExpr(e.thenBr)} : ${elsePart})`;
      }

      case 'ETuple':
        return `[${emitExpr(e.a)}, ${emitExpr(e.b)}] as const`;

      case 'EList':
        return `[${e.items.map(emitExpr).join(', ')}]`;

      case 'EMatch':
        return emitMatch(e);

      default:
        throw new Error(`Unknown expression kind: ${e.kind}`);
    }
  }

  function gatherApp(e) {
    const args = [];
    let node = e;
    while (node.kind === 'EApp') {
      args.unshift(node.arg);
      node = node.f;
    }
    return { head: node, args };
  }

  function emitMatch(e) {
    const scrut = `__scrut_${Math.random().toString(36).slice(2, 7)}`;
    const armLines = e.arms.map(arm => emitMatchArm(scrut, arm.pat, arm.body));
    return `(() => {\n  const ${scrut} = ${emitExpr(e.scrut)};\n${armLines.join('\n')}\n  throw new Error(\`Non-exhaustive match\`);\n})()`;
  }

  function emitMatchArm(scrut, pat, body) {
    const { cond, binds } = patternToCondBinds(scrut, pat);
    const bindLines = binds.map(([n, expr]) => `    const ${safeIdent(n)} = ${expr};`).join('\n');
    const condStr = cond || 'true';
    return `  if (${condStr}) {\n${bindLines}\n    return ${emitExpr(body)};\n  }`;
  }

  function patternToCondBinds(scrut, pat) {
    switch (pat.kind) {
      case 'PWild': return { cond: null, binds: [] };
      case 'PVar': return { cond: null, binds: [[pat.name, scrut]] };
      case 'PUnit': return { cond: null, binds: [] };
      case 'PNil': return { cond: `${scrut}.length === 0`, binds: [] };
      case 'PLitInt': return { cond: `${scrut} === ${pat.n}`, binds: [] };
      case 'PLitStr': return { cond: `${scrut} === \`${escapeStr(pat.s)}\``, binds: [] };
      case 'PLitBool': return { cond: `${scrut} === ${pat.b}`, binds: [] };
      case 'PCons': {
        const headBinds = pat.head.kind === 'PVar' ? [[pat.head.name, `${scrut}[0]`]] :
                          pat.head.kind === 'PWild' ? [] : [];
        const tailBinds = pat.tail.kind === 'PVar' ? [[pat.tail.name, `${scrut}.slice(1)`]] :
                          pat.tail.kind === 'PWild' ? [] : [];
        return { cond: `${scrut}.length > 0`, binds: [...headBinds, ...tailBinds] };
      }
      case 'PCon': {
        const cond = `${scrut}?._tag === \`${pat.name}\``;
        const binds = pat.args.map((a, i) => {
          if (a.kind === 'PVar') return [a.name, `(${scrut} as any)._${i}`];
          return null;
        }).filter(Boolean);
        return { cond, binds };
      }
      default: return { cond: null, binds: [] };
    }
  }

  function emitDecl(d) {
    switch (d.kind) {
      case 'DImport': return `// import ${d.parts.join('.')}`;
      case 'DExport': return emitDecl(d.inner);
      case 'DOpaque': return `type ${d.name} = unknown;`;
      case 'DExternal': return `declare function ${safeIdent(d.name)}(...args: any[]): any;`;

      case 'DType': {
        const tpStr = d.tvars.length > 0 ? `<${d.tvars.join(', ')}>` : '';
        const variants = d.ctors.map(c => {
          if (c.args.length === 0) return `{ _tag: \`${c.name}\` }`;
          const fields = c.args.map((t, i) => `_${i}: ${emitType(t)}`).join('; ');
          return `{ _tag: \`${c.name}\`; ${fields} }`;
        }).join(' | ');
        const typeDecl = `type ${d.name}${tpStr} = ${variants};`;

        const ctorDecls = d.ctors.map(c => {
          if (c.args.length === 0) {
            return `const ${safeIdent(c.name)}${tpStr}: ${d.name}${tpStr} = { _tag: \`${c.name}\` as const };`;
          }
          const params = c.args.map((t, i) => `_${i}: ${emitType(t)}`).join(', ');
          const obj = c.args.map((_, i) => `_${i}`).join(', ');
          return `const ${safeIdent(c.name)} = ${tpStr}(${params}): ${d.name}${tpStr} => ({ _tag: \`${c.name}\` as const, ${obj} });`;
        }).join('\n');

        return `${typeDecl}\n${ctorDecls}`;
      }

      case 'DFn': {
        const name = safeIdent(d.name);
        if (d.params.length === 0) {
          return `const ${name} = ${emitExpr(d.body)};`;
        }
        const paramStr = d.params.map(p => `${safeIdent(p.name)}: ${emitType(p.type)}`).join('): (');
        return `const ${name} = (${paramStr}): any => ${emitExpr(d.body)};`;
      }

      default:
        return `// unknown decl: ${d.kind}`;
    }
  }

  // TypeScript prelude (for display)
  const TS_PRELUDE = `// --- ll-lang TypeScript runtime helpers ---
const listLen = (xs: any[]): number => xs.length;
const listMap = (f: (x: any) => any) => (xs: any[]): any[] => xs.map(f);
const listFilter = (p: (x: any) => boolean) => (xs: any[]): any[] => xs.filter(p);
const listFold = (f: (acc: any) => (x: any) => any) => (z: any) => (xs: any[]): any => xs.reduce((a: any, x: any) => f(a)(x), z);
const listFoldRight = (f: (x: any) => (acc: any) => any) => (z: any) => (xs: any[]): any => xs.reduceRight((a: any, x: any) => f(x)(a), z);
const listReverse = (xs: any[]): any[] => [...xs].reverse();
const listAppend = (xs: any[]) => (ys: any[]): any[] => [...xs, ...ys];
const listConcat = (xss: any[][]): any[] => xss.flat();
const listIsEmpty = (xs: any[]): boolean => xs.length === 0;
const listHead = (xs: any[]): any | null => xs.length > 0 ? xs[0] : null;
const listTail = (xs: any[]): any[] | null => xs.length > 0 ? xs.slice(1) : null;
const listTake = (n: number) => (xs: any[]): any[] => xs.slice(0, n);
const listDrop = (n: number) => (xs: any[]): any[] => xs.slice(n);
const strLen = (s: string): number => s.length;
const strConcat = (a: string) => (b: string): string => a + b;
const strChars = (s: string): string[] => Array.from(s);
const strFromChars = (cs: string[]): string => cs.join('');
const strSlice = (s: string) => (start: number) => (len: number): string => s.slice(start, start + len);
const strTrim = (s: string): string => s.trim();
const strContains = (needle: string) => (hay: string): boolean => hay.includes(needle);
const strSplit = (sep: string) => (s: string): string[] => s.split(sep);
const strIndexOf = (needle: string) => (hay: string): number => hay.indexOf(needle);
const strReverse = (s: string): string => Array.from(s).reverse().join('');
const strToInt = (s: string): number | null => { const n = parseInt(s, 10); return isNaN(n) ? null : n; };
const strToFloat = (s: string): number | null => { const n = parseFloat(s); return isNaN(n) ? null : n; };
const intToStr = (n: number): string => String(n);
const floatToStr = (f: number): string => f.toString();
const abs = (x: number): number => Math.abs(x);
const sqrt = (x: number): number => Math.sqrt(x);
const charToInt = (c: string): number => c.charCodeAt(0);
const charIsDigit = (c: string): boolean => c >= '0' && c <= '9';
const charIsAlpha = (c: string): boolean => /[a-zA-Z]/.test(c);
const charIsSpace = (c: string): boolean => c === ' ' || c === '\\t' || c === '\\n' || c === '\\r';
const printfn = (s: string): void => { __ll_output(String(s) + '\\n'); };
const print = (s: string): void => { __ll_output(String(s)); };
const maybeWithDefault = (d: any) => (m: any): any => m !== null && m !== undefined ? m : d;
const Some = (_0: any) => ({ _tag: \`Some\` as const, _0 });
const None: any = { _tag: \`None\` as const };
const isNone = (m: any): boolean => m === null || m === undefined || (m && m._tag === 'None');
const isSome = (m: any): boolean => !isNone(m);
// --- end prelude ---`;

  // Plain JavaScript prelude (for execution — no type annotations)
  const JS_PRELUDE = `// ll-lang runtime
const listLen = (xs) => xs.length;
const listMap = (f) => (xs) => xs.map(f);
const listFilter = (p) => (xs) => xs.filter(p);
const listFold = (f) => (z) => (xs) => xs.reduce((a, x) => f(a)(x), z);
const listFoldRight = (f) => (z) => (xs) => xs.reduceRight((a, x) => f(x)(a), z);
const listReverse = (xs) => [...xs].reverse();
const listAppend = (xs) => (ys) => [...xs, ...ys];
const listConcat = (xss) => xss.flat();
const listIsEmpty = (xs) => xs.length === 0;
const listHead = (xs) => xs.length > 0 ? xs[0] : null;
const listTail = (xs) => xs.length > 0 ? xs.slice(1) : null;
const listTake = (n) => (xs) => xs.slice(0, n);
const listDrop = (n) => (xs) => xs.slice(n);
const strLen = (s) => s.length;
const strConcat = (a) => (b) => a + b;
const strChars = (s) => Array.from(s);
const strFromChars = (cs) => cs.join('');
const strSlice = (s) => (start) => (len) => s.slice(start, start + len);
const strTrim = (s) => s.trim();
const strContains = (needle) => (hay) => hay.includes(needle);
const strSplit = (sep) => (s) => s.split(sep);
const strIndexOf = (needle) => (hay) => hay.indexOf(needle);
const strReverse = (s) => Array.from(s).reverse().join('');
const strToInt = (s) => { const n = parseInt(s, 10); return isNaN(n) ? null : n; };
const strToFloat = (s) => { const n = parseFloat(s); return isNaN(n) ? null : n; };
const intToStr = (n) => String(n);
const floatToStr = (f) => f.toString();
const abs = (x) => Math.abs(x);
const sqrt = (x) => Math.sqrt(x);
const charToInt = (c) => c.charCodeAt(0);
const charIsDigit = (c) => c >= '0' && c <= '9';
const charIsAlpha = (c) => /[a-zA-Z]/.test(c);
const charIsSpace = (c) => c === ' ' || c === '\\t' || c === '\\n' || c === '\\r';
const printfn = (s) => { __ll_output(String(s) + '\\n'); };
const print = (s) => { __ll_output(String(s)); };
const maybeWithDefault = (d) => (m) => m !== null && m !== undefined ? m : d;
const Some = (_0) => ({ _tag: 'Some', _0 });
const None = { _tag: 'None' };
const isNone = (m) => m === null || m === undefined || (m && m._tag === 'None');
const isSome = (m) => !isNone(m);`;

  // JavaScript emitter (no type annotations — for execution)
  function emitDeclJs(d) {
    switch (d.kind) {
      case 'DImport': return `// import ${d.parts.join('.')}`;
      case 'DExport': return emitDeclJs(d.inner);
      case 'DOpaque': return `// opaque type ${d.name}`;
      case 'DExternal': return `// external ${d.name}`;

      case 'DType': {
        const ctorDecls = d.ctors.map(c => {
          if (c.args.length === 0) {
            return `const ${safeIdent(c.name)} = { _tag: '${c.name}' };`;
          }
          const params = c.args.map((_, i) => `_${i}`).join(', ');
          const obj = c.args.map((_, i) => `_${i}: _${i}`).join(', ');
          return `const ${safeIdent(c.name)} = (${params}) => ({ _tag: '${c.name}', ${obj} });`;
        }).join('\n');
        return ctorDecls;
      }

      case 'DFn': {
        const name = safeIdent(d.name);
        if (d.params.length === 0) {
          return `const ${name} = ${emitExpr(d.body)};`;
        }
        const paramStr = d.params.map(p => safeIdent(p.name)).join(') => (');
        return `const ${name} = (${paramStr}) => ${emitExpr(d.body)};`;
      }

      default:
        return `// unknown decl: ${d.kind}`;
    }
  }

  function emitModuleJs(mod) {
    const body = mod.decls.map(emitDeclJs).filter(Boolean).join('\n\n');
    return `${JS_PRELUDE}\n\n${body}`;
  }

  function emitModule(mod) {
    const modName = mod.path.join('.');
    const header = `// @ts-nocheck\n// Generated by lllc (ll-lang TypeScript backend)\n// Module: ${modName}`;
    const body = mod.decls.map(emitDecl).filter(Boolean).join('\n\n');
    return `${header}\n\n${TS_PRELUDE}\n\n${body}`;
  }

  // ─── PUBLIC API ─────────────────────────────────────────────────────────────

  function compile(source) {
    try {
      const tokens = tokenize(source);
      const parser = Parser(tokens);
      const mod = parser.parseModule();
      const ts = emitModule(mod);
      const js = emitModuleJs(mod);
      return { ok: true, ts, js, errors: [] };
    } catch (e) {
      return { ok: false, ts: null, js: null, errors: [e.message || String(e)] };
    }
  }

  function runJs(jsCode) {
    try {
      const collected = [];
      const fn = new Function('__ll_output', jsCode);
      fn((s) => collected.push(String(s)));
      return { ok: true, output: collected.join('') };
    } catch (e) {
      return { ok: false, output: '', runtimeError: e.message || String(e) };
    }
  }

  function compileAndRun(source) {
    const compileResult = compile(source);
    if (!compileResult.ok) {
      return { ok: false, errors: compileResult.errors, output: '', ts: null };
    }
    const runResult = runJs(compileResult.js);
    return {
      ok: runResult.ok,
      errors: runResult.runtimeError ? [`Runtime error: ${runResult.runtimeError}`] : [],
      output: runResult.output,
      ts: compileResult.ts,
    };
  }

  return { compile, compileAndRun, tokenize };
})();

if (typeof module !== 'undefined' && module.exports) {
  module.exports = LllcBrowser;
}
