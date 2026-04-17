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

/* ---- I/O stubs (MVP: not wired, return empty) ----------------------- */

char* read_line(void) {
    char* empty = (char*)malloc(1);
    if (empty) empty[0] = '\0';
    return empty;
}

char* read_file(const char* path) {
    (void)path;
    char* empty = (char*)malloc(1);
    if (empty) empty[0] = '\0';
    return empty;
}

void write_file(const char* path, const char* content) {
    (void)path;
    (void)content;
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
