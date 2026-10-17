struct Node { int value; struct Node *next; };

int main(void) {
    struct Node *head = 0;
    int i;
    for (i = 5; i >= 1; i = i - 1) {
        struct Node *n = malloc(sizeof(struct Node));
        n->value = i;
        n->next = head;
        head = n;
    }
    int sum = 0;
    struct Node *p = head;
    while (p != 0) { sum = sum + p->value; p = p->next; }
    printf("sum=%d\n", sum);
    return 0;
}
