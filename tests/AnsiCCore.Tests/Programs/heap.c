#include <stdlib.h>
#include <stdio.h>

int main(void) {
    int *a = malloc(5 * sizeof(int));
    int i;
    int sum = 0;
    if (a == NULL) return 1;
    for (i = 0; i < 5; i = i + 1) a[i] = i * i;
    for (i = 0; i < 5; i = i + 1) sum = sum + a[i];
    free(a);
    printf("%d\n", sum);
    return 0;
}
