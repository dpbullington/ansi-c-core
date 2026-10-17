int is_prime(int n) {
    int d;
    if (n < 2) return 0;
    for (d = 2; d * d <= n; d = d + 1)
        if (n % d == 0) return 0;
    return 1;
}

int main(void) {
    int i;
    for (i = 2; i < 20; i = i + 1)
        if (is_prime(i)) printf("%d ", i);
    printf("\n");
    return 0;
}
