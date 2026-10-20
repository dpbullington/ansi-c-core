#include <stdio.h>

int main(void) {
    printf("[%5d]\n", 42);
    printf("[%-5d]\n", 42);
    printf("[%05d]\n", 42);
    printf("[%+d]\n", 42);
    printf("[%8.2f]\n", 3.14159);
    printf("[%.3f]\n", 2.5);
    printf("[%5s]\n", "hi");
    printf("[%-5s]\n", "hi");
    printf("[%x]\n", 255);
    return 0;
}
