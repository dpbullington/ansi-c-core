#include <string.h>
#include <stdio.h>

int main(void) {
    char *a = "banana";
    printf("len=%d\n", (int)strlen(a));
    if (strcmp(a, "banana") == 0) printf("equal\n");
    return 0;
}
