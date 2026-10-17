/* exit: 42 */
#include <stdio.h>

#define VERBOSE 1

int main(void) {
#if VERBOSE
    printf("verbose mode\n");
#endif
#ifdef VERBOSE
    printf("defined\n");
#endif
    return 42;
}
