int main(void) {
    int a[6]; int i; int j;
    a[0] = 5; a[1] = 2; a[2] = 9; a[3] = 1; a[4] = 5; a[5] = 3;
    for (i = 0; i < 6; i = i + 1)
        for (j = 0; j < 5 - i; j = j + 1)
            if (a[j] > a[j + 1]) { int t = a[j]; a[j] = a[j + 1]; a[j + 1] = t; }
    for (i = 0; i < 6; i = i + 1) printf("%d ", a[i]);
    printf("\n");
    return 0;
}
