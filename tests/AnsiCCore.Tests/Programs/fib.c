int main(void) {
    int a = 0; int b = 1; int i;
    for (i = 0; i < 10; i = i + 1) {
        int t;
        printf("%d ", a);
        t = a + b; a = b; b = t;
    }
    printf("\n");
    return 0;
}
