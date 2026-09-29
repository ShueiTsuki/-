using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

// 用法: ilprobe <assembly.dll> <TypeFullName> [memberKeywordFilter]
// 只读元数据，不加载程序集，因此不受依赖缺失影响。
if (args.Length < 2)
{
    Console.WriteLine("usage: ilprobe <dll> <Full.Type.Name> [filter]");
    return 1;
}

string path = args[0];
string target = args[1];
string? filter = args.Length > 2 ? args[2] : null;

using var fs = File.OpenRead(path);
using var pe = new PEReader(fs);
var md = pe.GetMetadataReader();

string TypeName(EntityHandle h) => h.Kind switch
{
    HandleKind.TypeDefinition => md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)h).Name),
    HandleKind.TypeReference => md.GetString(md.GetTypeReference((TypeReferenceHandle)h).Name),
    HandleKind.TypeSpecification => "<typespec>",
    _ => "<" + h.Kind + ">",
};

foreach (var tdh in md.TypeDefinitions)
{
    var td = md.GetTypeDefinition(tdh);
    string ns = md.GetString(td.Namespace);
    string name = md.GetString(td.Name);
    string full = ns.Length == 0 ? name : ns + "." + name;
    if (!string.Equals(full, target, StringComparison.Ordinal)) continue;

    Console.WriteLine($"=== {full} ===");
    Console.WriteLine($"基类: {TypeName(td.BaseType)}   特性: {td.Attributes}");

    Console.WriteLine("--- 字段 ---");
    foreach (var fh in td.GetFields())
    {
        var f = md.GetFieldDefinition(fh);
        string fn = md.GetString(f.Name);
        if (filter != null && !fn.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
        Console.WriteLine($"  {f.DecodeSignature(new NameProvider(), null)} {fn}");
    }

    Console.WriteLine("--- 属性 ---");
    foreach (var ph in td.GetProperties())
    {
        var p = md.GetPropertyDefinition(ph);
        string pn = md.GetString(p.Name);
        if (filter != null && !pn.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
        Console.WriteLine($"  {pn}");
    }

    Console.WriteLine("--- 方法 ---");
    foreach (var mh in td.GetMethods())
    {
        var m = md.GetMethodDefinition(mh);
        string mn = md.GetString(m.Name);
        if (filter != null && !mn.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
        if (mn.StartsWith("get_", StringComparison.Ordinal) || mn.StartsWith("set_", StringComparison.Ordinal)) continue;
        Console.WriteLine($"  {mn}");
    }
    return 0;
}

Console.WriteLine($"未找到类型 {target}");
return 2;

sealed class NameProvider : ISignatureTypeProvider<string, object?>
{
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => reader.GetString(reader.GetTypeDefinition(handle).Name);
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var tr = reader.GetTypeReference(handle);
        string ns = reader.GetString(tr.Namespace);
        return ns.Length == 0 ? reader.GetString(tr.Name) : ns + "." + reader.GetString(tr.Name);
    }
    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => "<spec>";
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";
    public string GetByReferenceType(string elementType) => "ref " + elementType;
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetGenericInstantiation(string genericType, System.Collections.Immutable.ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(",", typeArguments) + ">";
    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetPinnedType(string elementType) => elementType;
    public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
}
