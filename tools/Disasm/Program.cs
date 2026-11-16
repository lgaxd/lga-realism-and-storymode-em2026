// Static disassembler for IL2CPP methods. Reads method RVAs from Cpp2IL dummy assemblies
// ([Address(RVA=...)] attributes) and disassembles GameAssembly.dll with Iced, naming call
// targets. Reads files only; nothing from the game is loaded or executed.
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Iced.Intel;

if (args.Length < 3) { Console.Error.WriteLine("usage: Disasm <dummyDir> <GameAssembly.dll> <Type::Method> [maxBytes]"); return 1; }
var dummyDir = args[0]; var gameAsm = args[1]; var target = args[2];
int maxBytes = args.Length > 3 ? int.Parse(args[3]) : 4096;

var byRva = new Dictionary<ulong, string>();
var targets = new List<(string name, ulong rva)>();
foreach (var file in Directory.GetFiles(dummyDir, "*.dll"))
{
    using var fs = File.OpenRead(file);
    using var pe = new PEReader(fs);
    if (!pe.HasMetadata) continue;
    var md = pe.GetMetadataReader();
    foreach (var th in md.TypeDefinitions)
    {
        var t = md.GetTypeDefinition(th);
        var tn = TypeName(md, th);
        foreach (var mh in t.GetMethods())
        {
            var m = md.GetMethodDefinition(mh);
            var rva = ReadRva(md, m.GetCustomAttributes());
            if (rva == 0) continue;
            var full = tn + "::" + md.GetString(m.Name);
            byRva.TryAdd(rva, full);
            if (full == target || full.EndsWith("." + target) || full.EndsWith("+" + target)) targets.Add((full, rva));
        }
    }
}
Console.WriteLine($"// {byRva.Count} methods indexed");
if (targets.Count == 0) { Console.Error.WriteLine("target not found"); return 2; }

var bytes = File.ReadAllBytes(gameAsm);
using var gpe = new PEReader(new MemoryStream(bytes));
var imageBase = gpe.PEHeaders.PEHeader!.ImageBase;
var sorted = byRva.Keys.OrderBy(x => x).ToArray();

foreach (var (name, rva) in targets)
{
    var off = RvaToOffset(gpe, (int)rva);
    var idx = Array.BinarySearch(sorted, rva);
    var next = idx >= 0 && idx + 1 < sorted.Length ? sorted[idx + 1] : rva + (ulong)maxBytes;
    var len = (int)Math.Min((ulong)maxBytes, next - rva);
    Console.WriteLine($"\n===== {name}  RVA 0x{rva:X}  ({len} bytes)");
    var reader = new ByteArrayCodeReader(bytes, off, len);
    var dec = Decoder.Create(64, reader, imageBase + rva);
    var fmt = new IntelFormatter();
    var outp = new StringOutput();
    while (reader.CanReadByte)
    {
        var ins = dec.Decode();
        fmt.Format(ins, outp);
        var text = outp.ToStringAndReset();
        string note = "";
        if (ins.FlowControl is FlowControl.Call or FlowControl.UnconditionalBranch && ins.Op0Kind is OpKind.NearBranch64)
        {
            var dst = ins.NearBranchTarget - imageBase;
            if (byRva.TryGetValue(dst, out var n)) note = "   ; " + n;
        }
        if (ins.IsIPRelativeMemoryOperand)
        {
            var dst = ins.IPRelativeMemoryAddress - imageBase;
            if (byRva.TryGetValue(dst, out var n)) note = "   ; &" + n;
        }
        Console.WriteLine($"{ins.IP - imageBase:X8}  {text}{note}");
        if (ins.Mnemonic == Mnemonic.Int3) break;
    }
}
return 0;

static int RvaToOffset(PEReader pe, int rva)
{
    foreach (var s in pe.PEHeaders.SectionHeaders)
        if (rva >= s.VirtualAddress && rva < s.VirtualAddress + s.VirtualSize)
            return rva - s.VirtualAddress + s.PointerToRawData;
    throw new Exception("rva not mapped");
}

static ulong ReadRva(MetadataReader md, CustomAttributeHandleCollection attrs)
{
    foreach (var ah in attrs)
    {
        var a = md.GetCustomAttribute(ah);
        if (a.Constructor.Kind != HandleKind.MethodDefinition && a.Constructor.Kind != HandleKind.MemberReference) continue;
        string an;
        if (a.Constructor.Kind == HandleKind.MethodDefinition)
            an = md.GetString(md.GetTypeDefinition(md.GetMethodDefinition((MethodDefinitionHandle)a.Constructor).GetDeclaringType()).Name);
        else
        {
            var mr = md.GetMemberReference((MemberReferenceHandle)a.Constructor);
            an = mr.Parent.Kind == HandleKind.TypeReference ? md.GetString(md.GetTypeReference((TypeReferenceHandle)mr.Parent).Name)
               : mr.Parent.Kind == HandleKind.TypeDefinition ? md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)mr.Parent).Name) : "";
        }
        if (an != "AddressAttribute") continue;
        var br = md.GetBlobReader(a.Value);
        br.ReadUInt16(); // prolog
        var n = br.ReadUInt16();
        for (var i = 0; i < n; i++)
        {
            br.ReadByte(); var et = br.ReadByte(); // field/property, type
            var name = br.ReadSerializedString();
            var val = et == 0x0E ? br.ReadSerializedString() : null;
            if (name == "RVA" && val != null) return Convert.ToUInt64(val, 16);
        }
    }
    return 0;
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
