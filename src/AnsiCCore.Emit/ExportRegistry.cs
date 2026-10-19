using System.Reflection;
using AnsiCCore.Runtime.Library;
using AnsiCCore.Runtime.Memory;

namespace AnsiCCore.Emit;

/// <summary>
/// One <c>[CExport]</c>-annotated .NET method, described for code generation: how to call it and
/// how its C arguments map onto its parameters.
/// </summary>
public sealed record ExportedFunction(
    string CName,
    string DeclaringTypeFullName,
    string MethodName,
    bool TakesMemory,
    IReadOnlyList<string> ParameterClrTypes,
    bool IsVariadic,
    string ReturnClrType,
    bool ReturnsInteger);

/// <summary>
/// The transpiler's "import library": the set of C functions resolvable to precompiled .NET code,
/// discovered by reflecting over assemblies for methods annotated with <see cref="CExportAttribute"/>.
/// This is the static, link-time analog of a C object file's exported symbols.
/// </summary>
public sealed class ExportRegistry
{
    private readonly Dictionary<string, ExportedFunction> _byName;

    private ExportRegistry(Dictionary<string, ExportedFunction> byName) => _byName = byName;

    /// <summary>The default import library: the runtime's own C standard-library shims.</summary>
    public static ExportRegistry Default { get; } = FromAssemblies(typeof(CStdLib).Assembly);

    public ExportedFunction? Resolve(string cName) => _byName.GetValueOrDefault(cName);

    public static ExportRegistry FromAssemblies(params Assembly[] assemblies)
        => FromAssemblies((IEnumerable<Assembly>)assemblies);

    /// <summary>Builds a registry of the default C standard library plus the given linked assemblies.</summary>
    public static ExportRegistry FromLinkedAssemblies(IEnumerable<Assembly> linked)
        => FromAssemblies(linked.Prepend(typeof(CStdLib).Assembly));

    public static ExportRegistry FromAssemblies(IEnumerable<Assembly> assemblies)
    {
        var map = new Dictionary<string, ExportedFunction>(StringComparer.Ordinal);
        foreach (var assembly in assemblies.Distinct())
            foreach (var type in assembly.GetTypes())
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    foreach (var export in method.GetCustomAttributes<CExportAttribute>())
                        map[export.Name] = Describe(export.Name, method);
        return new ExportRegistry(map);
    }

    private static ExportedFunction Describe(string cName, MethodInfo method)
    {
        var parameters = method.GetParameters();
        int start = 0;
        bool takesMemory = parameters.Length > 0 && parameters[0].ParameterType == typeof(CMemory);
        if (takesMemory) start = 1;

        bool variadic = parameters.Length > 0
            && parameters[^1].ParameterType == typeof(object[])
            && parameters[^1].IsDefined(typeof(ParamArrayAttribute), inherit: false);
        int end = variadic ? parameters.Length - 1 : parameters.Length;

        var fixedTypes = new List<string>();
        for (int i = start; i < end; i++)
            fixedTypes.Add(ClrName(parameters[i].ParameterType));

        return new ExportedFunction(
            cName,
            method.DeclaringType!.FullName!,
            method.Name,
            takesMemory,
            fixedTypes,
            variadic,
            ClrName(method.ReturnType),
            IsIntegerClrType(method.ReturnType));
    }

    private static bool IsIntegerClrType(Type t) => t == typeof(sbyte) || t == typeof(byte)
        || t == typeof(short) || t == typeof(ushort) || t == typeof(int) || t == typeof(uint)
        || t == typeof(long) || t == typeof(ulong);

    /// <summary>The C# spelling the emitter uses for a parameter/return type.</summary>
    private static string ClrName(Type t)
    {
        if (t == typeof(void)) return "void";
        if (t == typeof(sbyte)) return "sbyte";
        if (t == typeof(byte)) return "byte";
        if (t == typeof(short)) return "short";
        if (t == typeof(ushort)) return "ushort";
        if (t == typeof(int)) return "int";
        if (t == typeof(uint)) return "uint";
        if (t == typeof(long)) return "long";
        if (t == typeof(ulong)) return "ulong";
        if (t == typeof(float)) return "float";
        if (t == typeof(double)) return "double";
        if (t == typeof(bool)) return "bool";
        if (t == typeof(object)) return "object";
        if (t == typeof(CPtr)) return "AnsiCCore.Runtime.Memory.CPtr";
        return t.FullName ?? t.Name;
    }
}
