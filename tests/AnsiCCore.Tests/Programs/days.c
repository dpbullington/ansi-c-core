char *dayname(int d) {
    switch (d) {
        case 0: return "Sun";
        case 1: return "Mon";
        case 2: return "Tue";
        case 3: return "Wed";
        case 4: return "Thu";
        case 5: return "Fri";
        case 6: return "Sat";
        default: return "?";
    }
}

int main(void) {
    int i;
    for (i = 0; i < 8; i = i + 1) printf("%s\n", dayname(i));
    return 0;
}
