#include <stdio.h>

struct RGB { unsigned r : 8; unsigned g : 8; unsigned b : 8; };

int main(void) {
    struct RGB c;
    c.r = 255; c.g = 128; c.b = 64;
    printf("#%x%x%x size=%d\n", c.r, c.g, c.b, (int)sizeof(struct RGB));
    return 0;
}
