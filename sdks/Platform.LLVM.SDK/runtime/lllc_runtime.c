/*
 * lllc_runtime.c — minimal C runtime for ll-lang LLVM backend (MVP).
 *
 * Implements the externals that CodegenLLVM.fs actually emits at call sites
 * for simple hello-world / string programs. Names match the camelCase the
 * codegen emits (e.g. `strConcat`, not `str_concat`).
 *
 * Other runtime helpers declared in sdks/Platform.LLVM.SDK/src/Runtime.lll
 * are stubbed to zero/null — they compile and link but aren't exercised by
 * hello-world. Extend as codegen starts emitting them.
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <unistd.h>
#include <fcntl.h>
#include <sys/stat.h>
#include <errno.h>

/* ---- Core I/O -------------------------------------------------------- */

/* printfn: print string followed by newline. Used for `printfn "..."`. */
void printfn(const char* s) {
    if (s == NULL) {
        puts("(null)");
    } else {
        puts(s);
    }
}

/* print_str: print string without newline. */
void print_str(const char* s) {
    if (s == NULL) return;
    fputs(s, stdout);
}

/* print_int: print integer followed by newline. */
void print_int(int64_t n) {
    printf("%lld\n", (long long)n);
}

/* console_log: alias of printfn for JS-style logging. */
void console_log(const char* s) {
    printfn(s);
}

/* ---- String operations ---------------------------------------------- */

/* strConcat / str_concat: allocate a new buffer with a ++ b. */
char* strConcat(const char* a, const char* b) {
    if (a == NULL) a = "";
    if (b == NULL) b = "";
    size_t la = strlen(a);
    size_t lb = strlen(b);
    char* out = (char*)malloc(la + lb + 1);
    if (out == NULL) return NULL;
    memcpy(out, a, la);
    memcpy(out + la, b, lb);
    out[la + lb] = '\0';
    return out;
}

char* str_concat(const char* a, const char* b) {
    return strConcat(a, b);
}

/* strLen / str_len: length of null-terminated string. */
int64_t strLen(const char* s) {
    if (s == NULL) return 0;
    return (int64_t)strlen(s);
}

int64_t str_len(const char* s) {
    return strLen(s);
}

/* strEq / str_eq: structural string equality. */
int8_t strEq(const char* a, const char* b) {
    if (a == NULL && b == NULL) return 1;
    if (a == NULL || b == NULL) return 0;
    return (int8_t)(strcmp(a, b) == 0);
}

int8_t str_eq(const char* a, const char* b) {
    return strEq(a, b);
}

/* intToStr / int_to_str / str_from_int: format integer to new string. */
char* intToStr(int64_t n) {
    char buf[32];
    int len = snprintf(buf, sizeof(buf), "%lld", (long long)n);
    if (len < 0) return NULL;
    char* out = (char*)malloc((size_t)len + 1);
    if (out == NULL) return NULL;
    memcpy(out, buf, (size_t)len + 1);
    return out;
}

char* int_to_str(int64_t n) {
    return intToStr(n);
}

char* str_from_int(int64_t n) {
    return intToStr(n);
}

/* ---- I/O ------------------------------------------------------------- */

char* read_line(void) {
    char* empty = (char*)malloc(1);
    if (empty) empty[0] = '\0';
    return empty;
}

/* readFile: slurp an entire file into a freshly-malloc'd null-terminated
 * buffer. On any error (missing file, permission denied, read failure)
 * returns an empty string rather than crashing — matches the MVP
 * "no exceptions" contract. Trailing newlines are preserved verbatim
 * (callers use `printfn`, which adds its own). */
static char* empty_string(void) {
    char* out = (char*)malloc(1);
    if (out) out[0] = '\0';
    return out;
}

char* readFile(const char* path) {
    if (path == NULL) return empty_string();
    int fd = open(path, O_RDONLY);
    if (fd < 0) return empty_string();

    struct stat st;
    if (fstat(fd, &st) != 0) {
        close(fd);
        return empty_string();
    }

    size_t size = (size_t)st.st_size;
    char* buf = (char*)malloc(size + 1);
    if (buf == NULL) {
        close(fd);
        return empty_string();
    }

    size_t total = 0;
    while (total < size) {
        ssize_t n = read(fd, buf + total, size - total);
        if (n < 0) {
            if (errno == EINTR) continue;
            free(buf);
            close(fd);
            return empty_string();
        }
        if (n == 0) break; /* short file; treat as EOF */
        total += (size_t)n;
    }
    close(fd);
    buf[total] = '\0';
    return buf;
}

/* read_file: snake_case alias used by the Runtime.lll FFI mapping. */
char* read_file(const char* path) {
    return readFile(path);
}

/* writeFile: create/truncate `path` and write `content` (null-terminated).
 * Returns nothing useful; errors are silently swallowed to match the MVP
 * contract. Caller can `readFile` back to verify. */
void writeFile(const char* path, const char* content) {
    if (path == NULL) return;
    if (content == NULL) content = "";
    int fd = open(path, O_WRONLY | O_CREAT | O_TRUNC, 0644);
    if (fd < 0) return;

    size_t len = strlen(content);
    size_t total = 0;
    while (total < len) {
        ssize_t n = write(fd, content + total, len - total);
        if (n < 0) {
            if (errno == EINTR) continue;
            break;
        }
        total += (size_t)n;
    }
    close(fd);
}

/* write_file: snake_case alias used by the Runtime.lll FFI mapping. */
void write_file(const char* path, const char* content) {
    writeFile(path, content);
}

/* ---- Memory management stubs ---------------------------------------- */

/* Note: `malloc` / `free` from libc are used directly by codegen-emitted
 * calls (see `%raw = call ptr @malloc(...)` in CodegenLLVM.fs). We do not
 * redefine them here. `gc_alloc` / `gc_collect` are stubs until a real
 * collector lands. */
int64_t gc_alloc(int64_t size) {
    void* p = malloc((size_t)size);
    return (int64_t)(intptr_t)p;
}

void gc_collect(void) {
    /* no-op */
}

/* ---- ADT support stubs ---------------------------------------------- */

int64_t adt_alloc(int64_t tagval, int64_t numFields) {
    /* Layout: [tag:i64][field0:i64]...[fieldN-1:i64] */
    int64_t* p = (int64_t*)malloc(sizeof(int64_t) * (size_t)(numFields + 1));
    if (p == NULL) return 0;
    p[0] = tagval;
    return (int64_t)(intptr_t)p;
}

int64_t adt_tag(int64_t ptr) {
    if (ptr == 0) return 0;
    int64_t* p = (int64_t*)(intptr_t)ptr;
    return p[0];
}

int64_t adt_field(int64_t ptr, int64_t idx) {
    if (ptr == 0) return 0;
    int64_t* p = (int64_t*)(intptr_t)ptr;
    return p[idx + 1];
}

/* ---- List support stubs --------------------------------------------- */

int64_t list_nil(void) {
    return 0;
}

int64_t list_cons(int64_t head, int64_t tail) {
    int64_t* cell = (int64_t*)malloc(sizeof(int64_t) * 2);
    if (cell == NULL) return 0;
    cell[0] = head;
    cell[1] = tail;
    return (int64_t)(intptr_t)cell;
}

int64_t list_head(int64_t lst) {
    if (lst == 0) return 0;
    int64_t* p = (int64_t*)(intptr_t)lst;
    return p[0];
}

int64_t list_tail(int64_t lst) {
    if (lst == 0) return 0;
    int64_t* p = (int64_t*)(intptr_t)lst;
    return p[1];
}

int8_t list_is_empty(int64_t lst) {
    return (int8_t)(lst == 0);
}

/* ---- Codegen-internal allocator ------------------------------------- */

/* __ll_alloc: ADT cons-style allocator used by pattern-matching codegen.
 * Signature inferred from CodegenLLVM.fs:278:
 *   call ptr @__ll_alloc(i64 <tag>, i64 <payload>, ptr <tail>)
 * MVP stub: returns a 3-slot block with [tag][payload][tail]. */
void* __ll_alloc(int64_t tag, int64_t payload, void* tail) {
    int64_t* p = (int64_t*)malloc(sizeof(int64_t) * 3);
    if (p == NULL) return NULL;
    p[0] = tag;
    p[1] = payload;
    p[2] = (int64_t)(intptr_t)tail;
    return (void*)p;
}

/* ---- CLI arguments -------------------------------------------------- */

/* Captured from real C main; ll_getArgs() below reads these to synthesise
 * a cons list compatible with ll-lang's List[Str] ABI (tag=-1, payload=
 * string ptr as i64, tail=ptr). argv[0] (the program path) is intentionally
 * skipped — matches CodegenCSharp / F# tail-on-GetCommandLineArgs semantics,
 * minus the dotnet wrapper path that .NET prepends. */
static int   g_argc = 0;
static char** g_argv = NULL;

/* The post-processor renames the .ll's `@main` to `@ll_main`. This gives us
 * a single C entry point that captures argv, calls the user code, and
 * returns a real int to the OS. A weak `ll_main` stub keeps the link alive
 * when a .ll has no main (rare — every example currently has one). */
__attribute__((weak)) void ll_main(void) {}

int main(int argc, char** argv) {
    g_argc = argc;
    g_argv = argv;
    ll_main();
    return 0;
}

/* ll_getArgs: build a cons list of argv[1..argc-1] using the same heap-
 * node ABI as the frozen codegen: `{ i64 tag, i64 payload, ptr tail }`
 * with tag=-1 (LIST_CONS_TAG) and payload holding the string pointer cast
 * to i64. Strings are pointers into argv, which lives for the entire
 * process — no copying. Iteration is reversed so the resulting list has
 * argv[1] at head (matches how lllc builds list literals via cons). */
void* ll_getArgs(void) {
    void* tail = NULL;
    for (int i = g_argc - 1; i >= 1; i--) {
        int64_t* cell = (int64_t*)malloc(sizeof(int64_t) * 3);
        if (cell == NULL) return NULL;
        cell[0] = -1;                               /* LIST_CONS_TAG */
        cell[1] = (int64_t)(intptr_t)g_argv[i];     /* payload: Str ptr as i64 */
        cell[2] = (int64_t)(intptr_t)tail;          /* tail: ptr to next cell */
        tail = (void*)cell;
    }
    return tail;
}

/* ---- Extended runtime helpers (for lllcself stretch) ---------------------
 * All list functions assume the codegen's heap-node ABI:
 *   struct node { i64 tag; i64 payload; struct node* tail; }
 * with `tag == -1` (LIST_CONS_TAG) for cons cells and NULL for nil. Element
 * values are i64 in the payload slot (pointer-typed elements cast to i64).
 *
 * `strChars` returns a list of Char (payload = char zext to i64).
 * `strFromChars` consumes such a list and builds a C string.
 * -----------------------------------------------------------------------*/

#define LIST_CONS_TAG ((int64_t)-1)

typedef struct ll_node {
    int64_t tag;
    int64_t payload;
    struct ll_node* tail;
} ll_node_t;

static ll_node_t* ll_cons(int64_t payload, ll_node_t* tail) {
    ll_node_t* n = (ll_node_t*)malloc(sizeof(ll_node_t));
    if (n == NULL) return NULL;
    n->tag = LIST_CONS_TAG;
    n->payload = payload;
    n->tail = tail;
    return n;
}

/* Char / Int helpers ------------------------------------------------------ */

int8_t charIsDigit(int8_t c) {
    return (int8_t)(c >= '0' && c <= '9');
}

int8_t charIsSpace(int8_t c) {
    return (int8_t)(c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f' || c == '\v');
}

int64_t charToInt(int8_t c) {
    return (int64_t)(uint8_t)c;
}

int8_t intToChar(int64_t n) {
    return (int8_t)(n & 0xFF);
}

/* List helpers ------------------------------------------------------------ */

int8_t listIsEmpty(ll_node_t* lst) {
    return (int8_t)(lst == NULL);
}

int64_t listLen(ll_node_t* lst) {
    int64_t n = 0;
    while (lst != NULL) {
        n++;
        lst = lst->tail;
    }
    return n;
}

/* listReverse: return a reversed shallow copy. */
ll_node_t* listReverse(ll_node_t* lst) {
    ll_node_t* acc = NULL;
    while (lst != NULL) {
        acc = ll_cons(lst->payload, acc);
        lst = lst->tail;
    }
    return acc;
}

/* listAppend: non-destructive concat of two lists. */
ll_node_t* listAppend(ll_node_t* a, ll_node_t* b) {
    /* Reverse-a then prepend onto b. */
    ll_node_t* ra = listReverse(a);
    ll_node_t* out = b;
    while (ra != NULL) {
        out = ll_cons(ra->payload, out);
        ra = ra->tail;
    }
    return out;
}

/* listConcat: flatten a list-of-lists. Each payload is itself a list ptr. */
ll_node_t* listConcat(ll_node_t* lst) {
    ll_node_t* out = NULL;
    /* Walk `lst` once to get a reversed list of sublists, then prepend in
     * order so the final flat list preserves original element order. */
    ll_node_t* ra = listReverse(lst);
    while (ra != NULL) {
        ll_node_t* sub = (ll_node_t*)(intptr_t)ra->payload;
        out = listAppend(sub, out);
        ra = ra->tail;
    }
    return out;
}

/* listMap: apply a function to each element.
 * `fn` is a raw function pointer `int64_t (*)(int64_t)` — payloads are
 * passed as i64 (caller guarantees element type matches fn's param type
 * up to i64-punning, which is how the codegen treats all list payloads). */
ll_node_t* listMap(int64_t (*fn)(int64_t), ll_node_t* lst) {
    /* Build the mapped list in reverse, then reverse to restore order. */
    ll_node_t* acc = NULL;
    while (lst != NULL) {
        int64_t mapped = fn(lst->payload);
        acc = ll_cons(mapped, acc);
        lst = lst->tail;
    }
    return listReverse(acc);
}

/* String helpers --------------------------------------------------------- */

/* strChars: string -> List[Char]. Each char zext to i64 as payload. */
ll_node_t* strChars(const char* s) {
    if (s == NULL) return NULL;
    /* Build reversed then flip so list order matches byte order. */
    ll_node_t* acc = NULL;
    for (const char* p = s; *p != '\0'; p++) {
        acc = ll_cons((int64_t)(uint8_t)(*p), acc);
    }
    return listReverse(acc);
}

/* strFromChars: List[Char] -> string. Payload is i64-zext'd char. */
char* strFromChars(ll_node_t* lst) {
    int64_t n = listLen(lst);
    char* buf = (char*)malloc((size_t)n + 1);
    if (buf == NULL) return NULL;
    int64_t i = 0;
    while (lst != NULL) {
        buf[i++] = (char)(lst->payload & 0xFF);
        lst = lst->tail;
    }
    buf[n] = '\0';
    return buf;
}

/* strContains: substring check. */
int8_t strContains(const char* hay, const char* needle) {
    if (hay == NULL || needle == NULL) return 0;
    return (int8_t)(strstr(hay, needle) != NULL);
}

/* strSlice(s, start, end): substring [start, end), byte-indexed.
 * Out-of-range indices are clamped. Returns a freshly malloc'd string. */
char* strSlice(const char* s, int64_t start, int64_t end) {
    if (s == NULL) return empty_string();
    int64_t len = (int64_t)strlen(s);
    if (start < 0) start = 0;
    if (end > len) end = len;
    if (start >= end) return empty_string();
    size_t n = (size_t)(end - start);
    char* out = (char*)malloc(n + 1);
    if (out == NULL) return NULL;
    memcpy(out, s + start, n);
    out[n] = '\0';
    return out;
}

/* strSplit(sep, s): split `s` on every occurrence of `sep`. Returns a
 * List[Str] with payload = Str ptr cast to i64. Empty segments are
 * preserved. A NULL/empty separator returns the whole string as a single
 * element. */
ll_node_t* strSplit(const char* sep, const char* s) {
    if (s == NULL) return NULL;
    if (sep == NULL || sep[0] == '\0') {
        /* Whole string as single element. */
        char* copy = strConcat(s, "");  /* cheap dup */
        return ll_cons((int64_t)(intptr_t)copy, NULL);
    }
    size_t sep_len = strlen(sep);
    ll_node_t* acc = NULL;
    const char* cur = s;
    while (1) {
        const char* hit = strstr(cur, sep);
        if (hit == NULL) {
            /* Emit the remainder and stop. */
            char* seg = strConcat(cur, "");
            acc = ll_cons((int64_t)(intptr_t)seg, acc);
            break;
        }
        size_t seg_len = (size_t)(hit - cur);
        char* seg = (char*)malloc(seg_len + 1);
        if (seg != NULL) {
            memcpy(seg, cur, seg_len);
            seg[seg_len] = '\0';
            acc = ll_cons((int64_t)(intptr_t)seg, acc);
        }
        cur = hit + sep_len;
    }
    return listReverse(acc);
}

/* strTrim: drop ASCII whitespace from both ends. */
char* strTrim(const char* s) {
    if (s == NULL) return empty_string();
    const char* start = s;
    while (*start != '\0' && charIsSpace((int8_t)*start)) start++;
    const char* end = s + strlen(s);
    while (end > start && charIsSpace((int8_t)*(end - 1))) end--;
    size_t n = (size_t)(end - start);
    char* out = (char*)malloc(n + 1);
    if (out == NULL) return NULL;
    memcpy(out, start, n);
    out[n] = '\0';
    return out;
}

/* strToInt: parse a decimal integer. Returns a Maybe[Int]-shaped pointer,
 * but we cannot synthesise the codegen's per-module Some/None tag here.
 * The safe, portable answer is NULL on failure — pattern matches that
 * test for None via `null` succeed. For `Some n`, callers that pattern
 * match on Some expect tag=ctorTag("Some"), which varies per module.
 *
 * MVP: return NULL unconditionally, which means all callers see `None`.
 * This is a known limitation until codegen or runtime agrees on a stable
 * ADT tag scheme. Callers that use strToIntWithDefault (fallback to 0)
 * still work. */
void* strToInt(const char* s) {
    (void)s;
    return NULL;
}

/* fileExists: best-effort file existence check. */
int8_t fileExists(const char* path) {
    if (path == NULL) return 0;
    struct stat st;
    return (int8_t)(stat(path, &st) == 0);
}

/* readLine: read one line from stdin. Returns Maybe[Str]-shaped pointer;
 * see strToInt comment for the Some/None tag caveat. We return NULL on
 * EOF (correct `None` semantics under null-as-nil convention) and also
 * return NULL on success because we can't build `Some s` here. */
void* readLine(int64_t unit) {
    (void)unit;
    /* Consume a line to preserve correct stream positioning even though
     * we can't return Some(s). */
    int c;
    while ((c = getchar()) != EOF && c != '\n') {
        (void)c;
    }
    return NULL;
}
