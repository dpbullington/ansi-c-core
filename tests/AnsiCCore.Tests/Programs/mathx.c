#include <math.h>
#include <stdio.h>

int main(void) {
    printf("%.4f\n", sqrt(2.0));
    printf("%.0f\n", pow(2.0, 10.0));
    printf("%.4f\n", sin(M_PI / 6.0));
    printf("%.0f %.0f\n", floor(3.7), ceil(3.2));
    printf("%.2f\n", fmod(10.0, 3.0));
    printf("%.4f\n", hypot(3.0, 4.0));
    return 0;
}
