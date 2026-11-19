// Dumps type/member signatures of a .NET assembly (metadata only, no code is loaded or executed).
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Collections.Immutable;

if (args.Length < 2) { Console.Error.WriteLine("usage: CodeMap <assembly.dll> <out.txt>"); return 1; }
using var fs = File.OpenRead(args[0]);
using var pe = new PEReader(fs);
var md = pe.GetMetadataReader();
var prov = new SigProvider(md);
using var w = new StreamWriter(args[1]);
foreach (var th in md.TypeDefinitions)
{
    var t = md.GetTypeDefinition(th);
    var name = TypeName(md, th);
    if (name.StartsWith("<")) continue;
    string kind = (t.Attributes & TypeAttributes.Interface) != 0 ? "interface" : "class";
    string bas = t.BaseType.IsNil ? "" : " : " + prov.Name(t.BaseType);
    w.WriteLine($"{kind} {name}{bas}");
    foreach (var fh in t.GetFields())
    {
        var f = md.GetFieldDefinition(fh);
        var fn = md.GetString(f.Name);
        if (fn.StartsWith("NativeFieldInfoPtr_") || fn.StartsWith("NativeMethodInfoPtr_") || fn.StartsWith("<")) continue;
        string st = (f.Attributes & FieldAttributes.Static) != 0 ? "static " : "";
        var offset = ReadNamedString(md, f.GetCustomAttributes(), "FieldOffsetAttribute", "Offset");
        w.WriteLine($"  F {st}{f.DecodeSignature(prov, null)} {fn}{(offset != null ? "  @" + offset : "")}");
    }
    foreach (var ph in t.GetProperties())
    {
        var p = md.GetPropertyDefinition(ph);
        var sig = p.DecodeSignature(prov, null);
        w.WriteLine($"  P {sig.ReturnType} {md.GetString(p.Name)}");
    }
    foreach (var mh in t.GetMethods())
    {
        var m = md.GetMethodDefinition(mh);
        var mn = md.GetString(m.Name);
        if (mn.StartsWith("get_") || mn.StartsWith("set_")) continue;
        var sig = m.DecodeSignature(prov, null);
        var pnames = m.GetParameters().Select(h => md.GetParameter(h)).Where(p => p.SequenceNumber > 0).OrderBy(p => p.SequenceNumber).Select(p => md.GetString(p.Name)).ToList();
        var ps = sig.ParameterTypes.Select((pt, i) => pt + " " + (i < pnames.Count ? pnames[i] : "")).ToList();
        string st = (m.Attributes & MethodAttributes.Static) != 0 ? "static " : "";
        w.WriteLine($"  M {st}{sig.ReturnType} {mn}({string.Join(", ", ps)})");
    }
}
return 0;

static string? ReadNamedString(MetadataReader md, CustomAttributeHandleCollection attrs, string attrName, string argName)
{
    foreach (var ah in attrs)
    {
        var a = md.GetCustomAttribute(ah);
        string an = a.Constructor.Kind switch
        {
            HandleKind.MethodDefinition => md.GetString(md.GetTypeDefinition(md.GetMethodDefinition((MethodDefinitionHandle)a.Constructor).GetDeclaringType()).Name),
            HandleKind.MemberReference => md.GetMemberReference((MemberReferenceHandle)a.Constructor).Parent is var p && p.Kind == HandleKind.TypeReference
                ? md.GetString(md.GetTypeReference((TypeReferenceHandle)p).Name)
                : p.Kind == HandleKind.TypeDefinition ? md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)p).Name) : "",
            _ => ""
        };
        if (an != attrName) continue;
        var br = md.GetBlobReader(a.Value);
        br.ReadUInt16();
        var n = br.ReadUInt16();
        for (var i = 0; i < n; i++)
        {
            br.ReadByte(); var et = br.ReadByte();
            var name = br.ReadSerializedString();
            var val = et == 0x0E ? br.ReadSerializedString() : null;
            if (name == argName) return val;
        }
    }
    return null;
}

static string TypeName(MetadataReader md, TypeDefinitionHandle h)
{
    var t = md.GetTypeDefinition(h);
    var n = md.GetString(t.Name);
    var dt = t.GetDeclaringType();
    if (!dt.IsNil) return TypeName(md, dt) + "+" + n;
    var ns = md.GetString(t.Namespace);
    return string.IsNullOrEmpty(ns) ? n : ns + "." + n;
}

class SigProvider(MetadataReader md) : ISignatureTypeProvider<string, object?>
{
    public string Name(EntityHandle h) => h.Kind switch
    {
        HandleKind.TypeDefinition => GetTypeFromDefinition(md, (TypeDefinitionHandle)h, 0),
        HandleKind.TypeReference => GetTypeFromReference(md, (TypeReferenceHandle)h, 0),
        HandleKind.TypeSpecification => GetTypeFromSpecification(md, null, (TypeSpecificationHandle)h, 0),
        _ => "?"
    };
    public string GetArrayType(string e, ArrayShape s) => e + "[" + new string(',', s.Rank - 1) + "]";
    public string GetByReferenceType(string e) => "ref " + e;
    public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr";
    public string GetGenericInstantiation(string g, ImmutableArray<string> a) => g + "<" + string.Join(", ", a) + ">";
    public string GetGenericMethodParameter(object? c, int i) => "!!" + i;
    public string GetGenericTypeParameter(object? c, int i) => "!" + i;
    public string GetModifiedType(string m, string u, bool r) => u;
    public string GetPinnedType(string e) => e;
    public string GetPointerType(string e) => e + "*";
    public string GetPrimitiveType(PrimitiveTypeCode t) => t.ToString().ToLower();
    public string GetSZArrayType(string e) => e + "[]";
    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k) { var t = r.GetTypeDefinition(h); return r.GetString(t.Name); }
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k) { var t = r.GetTypeReference(h); return r.GetString(t.Name); }
    public string GetTypeFromSpecification(MetadataReader r, object? c, TypeSpecificationHandle h, byte k) => r.GetTypeSpecification(h).DecodeSignature(this, c);
}
