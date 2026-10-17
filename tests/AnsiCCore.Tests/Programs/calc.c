int add(int a, int b) { return a + b; }
int mul(int a, int b) { return a * b; }
int apply(int (*f)(int, int), int x, int y) { return f(x, y); }

int main(void) {
    printf("%d %d\n", apply(add, 3, 4), apply(mul, 3, 4));
    return 0;
}
