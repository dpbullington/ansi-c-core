#include <stdio.h>

#define GREETING "Hello from the preprocessor!"
#define SQUARE(x) ((x) * (x))

int main(void) {
    printf("%s\n", GREETING);
    printf("square of 5 is %d\n", SQUARE(5));
    return 0;
}
