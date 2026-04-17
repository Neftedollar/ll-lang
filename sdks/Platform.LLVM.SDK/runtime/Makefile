CC      ?= clang
CFLAGS  ?= -O2 -Wall -Wextra -fPIC

lllc_runtime.o: lllc_runtime.c
	$(CC) $(CFLAGS) -c -o lllc_runtime.o lllc_runtime.c

clean:
	rm -f lllc_runtime.o

.PHONY: clean
