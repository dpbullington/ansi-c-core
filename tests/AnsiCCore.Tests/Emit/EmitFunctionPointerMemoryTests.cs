namespace AnsiCCore.Tests.Emit;

/// <summary>
/// End-to-end tests for function pointers stored in the memory model: struct fields, arrays
/// (dispatch tables), address-taken variables, plus null/equality comparisons and reassignment.
/// Memory holds an opaque delegate token; reads cast it back to the delegate type.
/// </summary>
[TestFixture]
public class EmitFunctionPointerMemoryTests
{
    private static (int Exit, string Output) Run(string cSource)
        => EmitTestHost.RunCSharpWithOutput(EmitTestHost.TranspileWithDriver(cSource, "p.c", [EmitTestHost.IncludeDir()]));

    [Test]
    public void StructField_CalledThrough()
    {
        const string src = """
            #include <stdio.h>
            int add(int a, int b) { return a + b; }
            int sub(int a, int b) { return a - b; }
            struct Op { const char *name; int (*fn)(int, int); };
            int main(void)
            {
                struct Op ops[2];
                int i;
                ops[0].name = "add"; ops[0].fn = add;
                ops[1].name = "sub"; ops[1].fn = sub;
                for (i = 0; i < 2; i = i + 1)
                    printf("%s=%d\n", ops[i].name, ops[i].fn(7, 3));
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("add=10\nsub=4\n"));
    }

    [Test]
    public void ArrayOfFunctionPointers_DispatchTable()
    {
        const string src = """
            #include <stdio.h>
            int add(int a, int b) { return a + b; }
            int mul(int a, int b) { return a * b; }
            int main(void)
            {
                int (*table[2])(int, int);
                table[0] = add;
                table[1] = mul;
                printf("%d %d\n", table[0](10, 4), table[1](10, 4)); /* 14 40 */
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("14 40\n"));
    }

    [Test]
    public void AddressTakenVariable_CalledThroughDoublePointer()
    {
        const string src = """
            #include <stdio.h>
            int add(int a, int b) { return a + b; }
            int main(void)
            {
                int (*p)(int, int) = add;
                int (**pp)(int, int) = &p; /* taking &p forces p into memory */
                printf("%d\n", (*pp)(2, 40));
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("42\n"));
    }

    [Test]
    public void NullAndEqualityComparisons()
    {
        const string src = """
            #include <stdio.h>
            int inc(int x) { return x + 1; }
            struct S { int (*fn)(int); };
            int main(void)
            {
                struct S s;
                s.fn = 0;
                if (s.fn == 0) printf("null\n");
                s.fn = inc;
                if (s.fn == inc) printf("eq\n");
                printf("%d\n", s.fn(41));
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("null\neq\n42\n"));
    }

    [Test]
    public void StructFieldReassignment()
    {
        const string src = """
            #include <stdio.h>
            int one(void) { return 1; }
            int two(void) { return 2; }
            struct Box { int (*get)(void); };
            int main(void)
            {
                struct Box b;
                int sum;
                b.get = one;
                sum = b.get();
                b.get = two;
                sum = sum + b.get();
                printf("%d\n", sum); /* 1 + 2 = 3 */
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("3\n"));
    }

    [Test]
    public void QsortWithComparatorFromStructField()
    {
        // Combines the new capability with qsort: the comparator is read from a struct field.
        const string src = """
            #include <stdlib.h>
            #include <stdio.h>
            int asc(const void *a, const void *b) { return *(const int *)a - *(const int *)b; }
            struct Sorter { int (*cmp)(const void *, const void *); };
            int main(void)
            {
                struct Sorter s;
                int v[5];
                int i;
                s.cmp = asc;
                v[0] = 4; v[1] = 1; v[2] = 3; v[3] = 5; v[4] = 2;
                qsort(v, 5, sizeof(int), s.cmp);
                for (i = 0; i < 5; i = i + 1) printf("%d", v[i]);
                printf("\n");
                return 0;
            }
            """;
        Assert.That(Run(src).Output, Is.EqualTo("12345\n"));
    }
}
