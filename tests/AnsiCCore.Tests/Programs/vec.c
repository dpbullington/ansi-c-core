struct Vec { int x; int y; };

struct Vec vadd(struct Vec a, struct Vec b) {
    struct Vec r;
    r.x = a.x + b.x;
    r.y = a.y + b.y;
    return r;
}

int main(void) {
    struct Vec a; a.x = 1; a.y = 2;
    struct Vec b; b.x = 10; b.y = 20;
    struct Vec c = vadd(a, b);
    printf("%d,%d\n", c.x, c.y);
    return 0;
}
