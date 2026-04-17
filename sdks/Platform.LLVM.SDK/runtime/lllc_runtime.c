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
