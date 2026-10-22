#ifndef _ERRNO_H
#define _ERRNO_H

/* errno is a modifiable lvalue; it resolves to the program's per-run error cell, which the
   library shims (strtol/strtoul/strtod and the math functions) also write. */
int *__errno_location(void);
#define errno (*__errno_location())

#define EDOM   33
#define ERANGE 34

#endif
