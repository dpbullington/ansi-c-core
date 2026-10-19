#include <ctype.h>
#include <string.h>
#include <stdio.h>

int main(void) {
    char *s = "Hello, World 123";
    int letters = 0;
    int digits = 0;
    int i;
    for (i = 0; s[i] != 0; i = i + 1) {
        if (isalpha(s[i])) letters = letters + 1;
        if (isdigit(s[i])) digits = digits + 1;
    }
    printf("letters=%d digits=%d upper=%c\n", letters, digits, toupper('a'));
    if (strncmp(s, "Hello", 5) == 0) printf("prefix ok\n");
    return 0;
}
