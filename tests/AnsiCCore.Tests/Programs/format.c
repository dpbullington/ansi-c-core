#include <stdio.h>

int main(void) {
    char buf[64];
    int n = sprintf(buf, "%d-%s-%c", 7, "hi", 'Z');
    printf("[%s] len=%d\n", buf, n);
    fprintf(stdout, "direct to stdout: %d\n", 42);
    fputs("via fputs\n", stdout);
    return 0;
}
