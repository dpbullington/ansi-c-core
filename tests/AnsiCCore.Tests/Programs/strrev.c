int length(char *s) { int n = 0; while (s[n] != 0) n = n + 1; return n; }

int main(void) {
    char s[6];
    int n; int i;
    s[0] = 'h'; s[1] = 'e'; s[2] = 'l'; s[3] = 'l'; s[4] = 'o'; s[5] = 0;
    n = length(s);
    for (i = 0; i < n / 2; i = i + 1) {
        char t = s[i]; s[i] = s[n - 1 - i]; s[n - 1 - i] = t;
    }
    printf("%s\n", s);
    return 0;
}
