// ApiProbe — read-only .NET metadata / IL probe for Assembly-CSharp.dll.
//
// Purpose: verify real member names, visibility and signatures BEFORE a mod calls
// them. It never loads or executes the game assembly: everything comes from
// System.Reflection.Metadata + PEReader (metadata tables, signatures, IL bytes).
//
// Usage (run from the repo root, ps-chem-mod):
//   ..\.dotnet\dotnet.exe run --project tools\api-probe -c Release -- <assembly.dll> <outDir> [ilSelector ...]
//
//   <assembly.dll> : defaults to the installed game assembly (see DefaultAssemblyPath).
//   <outDir>       : defaults to <cwd>\tools\api-probe\out.
//   ilSelector     : "TypeRegex::MethodRegex" (regex, case-insensitive) or a bare
//                    method-name regex. Each match dumps a full IL listing to out\il\.
//
// Outputs: assembly.txt, types.txt, members.txt, focus-*.txt, strings.txt, il\*.
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;

namespace ApiProbe;

internal static class Program
{
    private const string DefaultAssemblyPath =
        @"D:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed\Assembly-CSharp.dll";

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        string asmPath = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : DefaultAssemblyPath;
        string outDir = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1])
            ? Path.GetFullPath(args[1])
            : Path.Combine(Directory.GetCurrentDirectory(), "tools", "api-probe", "out");
        var ilSelectors = new List<IlSelector>();
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i].StartsWith("--il=", StringComparison.Ordinal))
                ilSelectors.Add(new IlSelector(args[i].Substring("--il=".Length)));
            else
                ilSelectors.Add(new IlSelector(args[i]));
        }

        if (!File.Exists(asmPath))
        {
            Console.Error.WriteLine($"[api-probe] assembly not found: {asmPath}");
            return 2;
        }

        Directory.CreateDirectory(outDir);
        string ilDir = Path.Combine(outDir, "il");
        if (Directory.Exists(ilDir)) Directory.Delete(ilDir, recursive: true);
        Directory.CreateDirectory(ilDir);

        using var fs = new FileStream(asmPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var pe = new PEReader(fs);
        MetadataReader md = pe.GetMetadataReader();
        var tp = new TypeProvider(md);

        var focus = new FocusWriter(outDir);
        var ilCandidates = new List<MethodRef>();
        var stringHits = new List<(string Text, string Where)>();
        var methodCount = 0;
        var typeCount = 0;

        // ---------------------------------------------------------------- types + members
        using (var typesW = new StreamWriter(Path.Combine(outDir, "types.txt"), false, Utf8NoBom))
        using (var membersW = new StreamWriter(Path.Combine(outDir, "members.txt"), false, Utf8NoBom))
        {
            foreach (TypeDefinitionHandle tdh in md.TypeDefinitions)
            {
                TypeDefinition td = md.GetTypeDefinition(tdh);
                typeCount++;
                string typeFull = Names.TypeFullName(md, tdh);
                string typeKind = Names.TypeKind(md, td);
                string typeVis = Names.Visibility(td.Attributes);
                string baseName = Names.HandleTypeName(md, td.BaseType);
                var interfaces = td.GetInterfaceImplementations()
                    .Select(h => Names.HandleTypeName(md, md.GetInterfaceImplementation(h).Interface))
                    .ToList();
                string typeAttrs = DescribeTypeAttributes(md, tp, td);
                typesW.WriteLine($"{typeFull} | {typeVis} {typeKind} | base={baseName} | ifaces=[{string.Join(", ", interfaces)}]{typeAttrs}");

                if (Names.MatchesAny(typeFull, ModLoaderTypeRegexes))
                    focus.Add("focus-modloader.txt", $"{typeFull} | {typeVis} {typeKind} | base={baseName}{typeAttrs}");

                membersW.WriteLine();
                membersW.WriteLine($"### {typeFull}  ({typeVis}, {typeKind}, base={baseName}){typeAttrs}");

                foreach (FieldDefinitionHandle fdh in td.GetFields())
                {
                    FieldDefinition fd = md.GetFieldDefinition(fdh);
                    string line = FormatField(md, tp, typeFull, fd);
                    membersW.WriteLine("  F  " + line);
                    HandleFocus(focus, md, tp, typeFull, md.GetString(fd.Name), "F", line, typeFull);
                }

                foreach (PropertyDefinitionHandle pdh in td.GetProperties())
                {
                    PropertyDefinition pd = md.GetPropertyDefinition(pdh);
                    string line = FormatProperty(md, tp, pd);
                    membersW.WriteLine("  P  " + line);
                    HandleFocus(focus, md, tp, typeFull, md.GetString(pd.Name), "P", line, typeFull);
                }

                foreach (EventDefinitionHandle edh in td.GetEvents())
                {
                    EventDefinition ed = md.GetEventDefinition(edh);
                    string line = $"{Names.HandleTypeName(md, ed.Type)} {md.GetString(ed.Name)}";
                    membersW.WriteLine("  E  " + line);
                }

                foreach (MethodDefinitionHandle mdh in td.GetMethods())
                {
                    methodCount++;
                    MethodDefinition mdef = md.GetMethodDefinition(mdh);
                    string mname = md.GetString(mdef.Name);
                    string line = FormatMethod(md, tp, mdef);
                    membersW.WriteLine("  M  " + line);
                    HandleFocus(focus, md, tp, typeFull, mname, "M", line, typeFull);

                    ilCandidates.Add(new MethodRef(tdh, mdh, typeFull, mname, line));

                    foreach (IlSelector sel in ilSelectors)
                        if (sel.Matches(typeFull, mname))
                            sel.Hits.Add(new MethodRef(tdh, mdh, typeFull, mname, line));
                }
            }
        }

        // ---------------------------------------------------------------- assembly identity
        WriteAssemblyInfo(md, tp, pe, asmPath, Path.Combine(outDir, "assembly.txt"));

        // ---------------------------------------------------------------- all user strings + selected IL
        var xref = new SortedSet<string>(StringComparer.Ordinal);
        using (var stringsW = new StreamWriter(Path.Combine(outDir, "strings.txt"), false, Utf8NoBom))
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (MethodRef mr in ilCandidates)
            {
                MethodDefinition mdef = md.GetMethodDefinition(mr.Method);
                if (mdef.RelativeVirtualAddress == 0) continue;
                byte[] il;
                try
                {
                    il = pe.GetMethodBody(mdef.RelativeVirtualAddress).GetILContent().ToArray();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[api-probe] IL read failed for {mr.Type}::{mr.Name}: {ex.Message}");
                    continue;
                }

                foreach (var ins in IlDecoder.Decode(md, tp, il))
                {
                    if (ins.Kind == IlKind.UserString)
                    {
                        if (seen.Add(ins.Text!))
                            stringHits.Add((ins.Text!, $"{mr.Type}::{mr.Name}"));
                        stringsW.WriteLine($"{Escape(ins.Text!)}  <= {mr.Type}::{mr.Name}");
                    }
                    else if (ins.Operand.Length > 0 && XrefRegex.IsMatch(ins.Operand))
                    {
                        xref.Add($"{ins.Operand}  <=  {mr.Type}::{mr.Name}");
                    }
                }
            }
        }
        File.WriteAllText(Path.Combine(outDir, "xref.txt"), string.Join(Environment.NewLine, xref), Utf8NoBom);

        // Write one IL file per selector hit.
        var ilIndex = new StringBuilder();
        foreach (IlSelector sel in ilSelectors)
        {
            ilIndex.AppendLine($"=== selector: {sel.Raw} ({sel.Hits.Count} hit(s))");
            if (sel.Hits.Count == 0)
            {
                ilIndex.AppendLine("  !! NO MATCH — the member does not exist under this name/owner");
                focus.Add("focus-IL-no-match.txt", $"selector {sel.Raw}: NO MATCH");
            }
            foreach (MethodRef mr in sel.Hits)
            {
                MethodDefinition mdef = md.GetMethodDefinition(mr.Method);
                string safe = SafeFileName($"{mr.Type}__{mr.Name}");
                string path = Path.Combine(ilDir, safe + ".il.txt");
                int n = 2;
                while (File.Exists(path)) path = Path.Combine(ilDir, $"{safe}_{n++}.il.txt");
                ilIndex.AppendLine($"  {mr.Type}::{mr.Name}  ->  il\\{Path.GetFileName(path)}");
                using var w = new StreamWriter(path, false, Utf8NoBom);
                w.WriteLine($"// {mr.Type}::{mr.Name}");
                w.WriteLine($"// signature: {mr.Display}");
                if (mdef.RelativeVirtualAddress == 0)
                {
                    w.WriteLine("// <no IL body: abstract / extern / runtime-provided>");
                    continue;
                }
                byte[] il = pe.GetMethodBody(mdef.RelativeVirtualAddress).GetILContent().ToArray();
                w.WriteLine($"// IL bytes: {il.Length}");
                w.WriteLine();
                foreach (var ins in IlDecoder.Decode(md, tp, il))
                    w.WriteLine(ins.Render());
            }
        }
        File.WriteAllText(Path.Combine(outDir, "il-index.txt"), ilIndex.ToString(), Utf8NoBom);

        focus.Flush();
        Console.WriteLine($"[api-probe] assembly   : {asmPath} ({new FileInfo(asmPath).Length:N0} bytes)");
        Console.WriteLine($"[api-probe] types      : {typeCount:N0}");
        Console.WriteLine($"[api-probe] methods    : {methodCount:N0}");
        Console.WriteLine($"[api-probe] strings    : {stringHits.Count:N0} distinct ldstr");
        Console.WriteLine($"[api-probe] il dumped  : {ilSelectors.Sum(s => s.Hits.Count)} method(s)");
        Console.WriteLine($"[api-probe] output dir : {outDir}");
        return 0;
    }

    // ==================================================================== focus routing
    private static readonly Regex HeatRegex = new(
        "(heat|suspicion|crime|investigation|security|penalt|wanted|contraband|raid|raidable)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ModDataRegex = new("ModData", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SpriteAnyRegex = new("(sprite|icon)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Member references worth a cross-reference table (who calls / reads what).</summary>
    private static readonly Regex XrefRegex = new(
        "(get_version|VersionMatchesGame|NormalizeVersion|PlayerStore::VERSION|GameVersions|" +
        "CommitCrime|CheckCrime|spriteChanged|SetSprite|ResolveSpriteByName|ResolveItemSprite|ValidateSelectLocked|" +
        "spritePath|spriteAtlasPath|useSpriteFromMod|" +
        "evidenceLevel|crimeAmount|secData|suspicion|SEC_SUSPICION_PENALTY|" +
        // round 14: debug spawn / settings / custom UI / vanilla popup
        "ModConfigWindow|GenericConfirmation|ConfirmPanel|backInvinvElement|CanAccept|TryAcceptAll|ResponseWindow|" +
        "AddDebugFunction|RemoveDebugFunctions|DEBUG_CATEGORY|SpawnItem|MoveItemToBack|MoveItemToFront|GetConfig|" +
        "CustomUIManager::(Window|CreateWindow|GetElement)|UseMetaPrefabs|UseMachineryStyle|OnChanged|" +
        // round 15: harmony bridge
        "PourInto|PourOutOf|TransferLiquid|GetLiquidParts|WillAccept|ChemicalProductHelper|ChemScanner|" +
        "GetChemicalFeature|GetWaterFeature|SetPurityFeature|GetPurity|ItemCondition|CHEMICAL_PRODUCT|" +
        "StoreClient::|SellItemFromTable|OnItemsSold|HandleSoldItem|GetLiquidContainerValue|" +
        // round 16: printer target hooks / labeler / purity grades
        "ItemTargetItemFunc|onItemTarget|onThisTarget|CanTarget|MayTarget|Target\\(|" +
        "GetGradeNumber|GetGradeDifference|lab_pure|acceptedChemicalProductGrade|HandleLabelerWater|" +
        // round 17: containers / scanner water mode / phone / paper / grid
        "InitLiquidContainerItem|WaterPitcher|WaterJug|SetCoffeeBeanComposition|PourSpirit|" +
        "PhoneUIManager|StorePhoneClient|PaperBook|SpawnPaperItem|gridSize|RefreshWaterMode|" +
        // round 18: deep container/component/phone/power
        "CloneLinked|DirectoryMaster::Create|UpdateContainerGoodType|AddLiquidToChemFromList|TransferLiquidToChem|" +
        "GetTotalVolume|GetItemFeedbackLevel|GenerateReaction|InitLiquid|Liquid::GetLiquidFromId|" +
        "SetupBatterySlot|CreateMachineryWindow|MachineryHelper|ModItemDirectory)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] ModLoaderTypeRegexes =
    {
        "(?i)(LoadedMod|ModManifest|ModManager|ModLoader|ModDirectory|ModInfo|ModEntry|ModAPI|ModHandler|ModSprite)",
    };

    private static void HandleFocus(FocusWriter focus, MetadataReader md, TypeProvider tp,
        string typeFull, string memberName, string kind, string formattedLine, string owner)
    {
        string oneLine = $"{typeFull}::{memberName} | {kind} | {formattedLine}";

        if (HeatRegex.IsMatch(memberName))
            focus.Add("focus-heat.txt", oneLine);
        if (ModDataRegex.IsMatch(memberName) || ModDataRegex.IsMatch(typeFull))
            focus.Add("focus-moddata.txt", oneLine);

        if (typeFull.EndsWith("GameItem", StringComparison.Ordinal))
            focus.Add("focus-gameitem.txt", oneLine);

        if (typeFull.EndsWith("ModHelper", StringComparison.Ordinal) || typeFull.Equals("ModHelper", StringComparison.Ordinal))
            focus.Add("focus-modhelper.txt", oneLine);

        if (Names.MatchesAny(typeFull, ModLoaderTypeRegexes))
            focus.Add("focus-modloader.txt", oneLine);

        if (SpriteAnyRegex.IsMatch(memberName))
            focus.Add("focus-sprite-any.txt", oneLine);
    }

    // ==================================================================== type attributes
    private static string DescribeTypeAttributes(MetadataReader md, TypeProvider tp, TypeDefinition td)
    {
        var flags = new List<string>();
        if ((td.Attributes & TypeAttributes.Interface) != 0) flags.Add("interface");
        if ((td.Attributes & TypeAttributes.Abstract) != 0 && (td.Attributes & TypeAttributes.Sealed) != 0) flags.Add("static");
        foreach (CustomAttributeHandle cah in td.GetCustomAttributes())
        {
            string desc = Attributes.Describe(md, tp, cah);
            if (desc.Contains("ObsoleteAttribute")) flags.Add("obsolete" + desc);
            else if (desc.Contains("ExtensionAttribute")) flags.Add("extension-class");
        }
        return flags.Count == 0 ? "" : "  [" + string.Join(", ", flags) + "]";
    }

    // ==================================================================== formatting
    private static string FormatField(MetadataReader md, TypeProvider tp, string owner, FieldDefinition fd)
    {
        string vis = Names.FieldVisibility(fd.Attributes);
        string mods = "";
        if ((fd.Attributes & FieldAttributes.Static) != 0) mods += "static ";
        if ((fd.Attributes & FieldAttributes.Literal) != 0) mods += "const ";
        else if ((fd.Attributes & FieldAttributes.InitOnly) != 0) mods += "readonly ";
        string type = fd.DecodeSignature(tp, null);
        string attrs = Attributes.ShortFlags(md, tp, fd.GetCustomAttributes());
        string? constant = Constants.FieldConstant(md, fd);
        string constText = constant is null ? "" : $" = {constant}";
        return $"{vis} {mods}{type} {md.GetString(fd.Name)}{constText}{attrs}";
    }

    private static string FormatProperty(MetadataReader md, TypeProvider tp, PropertyDefinition pd)
    {
        var sig = pd.DecodeSignature(tp, null);
        PropertyAccessors acc = pd.GetAccessors();
        var parts = new List<string>();
        if (!acc.Getter.IsNil)
        {
            MethodDefinition g = md.GetMethodDefinition(acc.Getter);
            parts.Add(Names.MethodVisibility(g.Attributes) + " get;");
        }
        if (!acc.Setter.IsNil)
        {
            MethodDefinition s = md.GetMethodDefinition(acc.Setter);
            parts.Add(Names.MethodVisibility(s.Attributes) + " set;");
        }
        string type = sig.ReturnType;
        string indexParams = sig.ParameterTypes.Length > 0 ? "[" + string.Join(", ", sig.ParameterTypes) + "]" : "";
        string stat = !acc.Getter.IsNil && (md.GetMethodDefinition(acc.Getter).Attributes & MethodAttributes.Static) != 0 ? "static " : "";
        return $"{stat}{type} {md.GetString(pd.Name)}{indexParams} {{ {string.Join(" ", parts)} }}";
    }

    private static string FormatMethod(MetadataReader md, TypeProvider tp, MethodDefinition m)
    {
        var sig = m.DecodeSignature(tp, null);
        string vis = Names.MethodVisibility(m.Attributes);
        string mods = "";
        if ((m.Attributes & MethodAttributes.Static) != 0) mods += "static ";
        if ((m.Attributes & MethodAttributes.Abstract) != 0) mods += "abstract ";
        else if ((m.Attributes & MethodAttributes.Virtual) != 0) mods += "virtual ";
        if ((m.Attributes & MethodAttributes.SpecialName) != 0) mods += "specialname ";

        string generic = "";
        var gps = m.GetGenericParameters();
        if (gps.Count > 0)
        {
            generic = "<" + string.Join(", ", gps.Select(h => md.GetString(md.GetGenericParameter(h).Name))) + ">";
        }

        // Parameter names (and optional default values), keyed by sequence number.
        var names = new Dictionary<int, string>();
        var defaults = new Dictionary<int, string>();
        foreach (ParameterHandle ph in m.GetParameters())
        {
            Parameter p = md.GetParameter(ph);
            if (p.SequenceNumber <= 0) continue;
            names[p.SequenceNumber] = md.GetString(p.Name);
            string? d = Constants.ParameterConstant(md, p);
            if (d is not null) defaults[p.SequenceNumber] = d;
        }
        var ps = new List<string>();
        for (int i = 0; i < sig.ParameterTypes.Length; i++)
        {
            string pn = names.TryGetValue(i + 1, out string? n) && !string.IsNullOrEmpty(n) ? n : "arg" + i;
            string dv = defaults.TryGetValue(i + 1, out string? dd) ? " = " + dd : "";
            ps.Add(sig.ParameterTypes[i] + " " + pn + dv);
        }

        string attrs = Attributes.ShortFlags(md, tp, m.GetCustomAttributes());
        string decl = m.GetDeclaringType().IsNil ? "" : Names.TypeFullName(md, m.GetDeclaringType()) + ".";
        return $"{vis} {mods}{sig.ReturnType} {decl}{md.GetString(m.Name)}{generic}({string.Join(", ", ps)}){attrs}";
    }

    private static void WriteAssemblyInfo(MetadataReader md, TypeProvider tp, PEReader pe, string asmPath, string path)
    {
        using var w = new StreamWriter(path, false, Utf8NoBom);
        AssemblyDefinition asm = md.GetAssemblyDefinition();
        w.WriteLine("== assembly identity ==");
        w.WriteLine($"Name              : {md.GetString(asm.Name)}");
        w.WriteLine($"Version           : {asm.Version}");
        w.WriteLine($"Culture           : {md.GetString(asm.Culture)}");
        w.WriteLine($"PublicKey         : {asm.PublicKey.IsNil}");
        w.WriteLine($"Module            : {md.GetString(md.GetModuleDefinition().Name)}");
        w.WriteLine($"Mvid              : {md.GetGuid(md.GetModuleDefinition().Mvid)}");
        w.WriteLine();
        w.WriteLine("== .NET assembly custom attributes (incl. AssemblyInformationalVersion / AssemblyFileVersion) ==");
        foreach (CustomAttributeHandle cah in asm.GetCustomAttributes())
            w.WriteLine("  [assembly: " + Attributes.Describe(md, tp, cah) + "]");
        w.WriteLine();
        w.WriteLine("== PE / module versions (Win32 version resource is separate; these are COFF header versions) ==");
        var headers = pe.PEHeaders;
        PEHeader? peh = headers.PEHeader;
        w.WriteLine($"ImageVersion                 : {peh?.MajorImageVersion}.{peh?.MinorImageVersion}");
        w.WriteLine($"LinkerVersion                : {peh?.MajorLinkerVersion}.{peh?.MinorLinkerVersion}");
        w.WriteLine($"SubsystemVersion             : {peh?.MajorSubsystemVersion}.{peh?.MinorSubsystemVersion}");
        w.WriteLine($"CorHeader.MajorRuntimeVersion: {headers.CorHeader?.MajorRuntimeVersion}");
        w.WriteLine($"CorHeader.MinorRuntimeVersion: {headers.CorHeader?.MinorRuntimeVersion}");
        w.WriteLine($"Machine                      : {headers.CoffHeader.Machine}");
        w.WriteLine($"Characteristics              : {headers.CoffHeader.Characteristics}");
        w.WriteLine($"TimeDateStamp                : {headers.CoffHeader.TimeDateStamp} (unix seconds: {DateTimeOffset.FromUnixTimeSeconds(headers.CoffHeader.TimeDateStamp):yyyy-MM-dd HH:mm:ss} UTC)");
        w.WriteLine($"FileSizeBytes                : {new FileInfo(asmPath).Length:N0}");
        w.WriteLine();
        w.WriteLine("== InternalsVisibleTo (a mod CANNOT rely on internal members unless named here) ==");
        bool anyIvt = false;
        foreach (CustomAttributeHandle cah in asm.GetCustomAttributes())
        {
            string d = Attributes.Describe(md, tp, cah);
            if (d.Contains("InternalsVisibleTo"))
            {
                anyIvt = true;
                w.WriteLine("  " + d);
            }
        }
        if (!anyIvt) w.WriteLine("  <none>");
    }

    // ==================================================================== helpers
    internal static readonly UTF8Encoding Utf8NoBom = new(false);

    private static string SafeFileName(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' ? c : '_');
        string r = sb.ToString();
        return r.Length > 150 ? r.Substring(0, 150) : r;
    }

    internal static string Escape(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s)
            sb.Append(c switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\r' => "\\r",
                '\n' => "\\n",
                '\t' => "\\t",
                _ => c.ToString(),
            });
        sb.Append('"');
        return sb.ToString();
    }
}

// ======================================================================== member refs
internal sealed record MethodRef(TypeDefinitionHandle TypeDef, MethodDefinitionHandle Method, string Owner, string Name, string Display)
{
    public string Type => Owner;
}

// ======================================================================== IL selectors
internal sealed class IlSelector
{
    public string Raw { get; }
    public List<MethodRef> Hits { get; } = new();
    private readonly Regex _typeRegex;
    private readonly Regex _methodRegex;

    public IlSelector(string raw)
    {
        Raw = raw;
        int sep = raw.IndexOf("::", StringComparison.Ordinal);
        if (sep >= 0)
        {
            _typeRegex = new Regex(raw.Substring(0, sep), RegexOptions.IgnoreCase);
            _methodRegex = new Regex(raw.Substring(sep + 2), RegexOptions.IgnoreCase);
        }
        else
        {
            _typeRegex = new Regex(".*", RegexOptions.IgnoreCase);
            _methodRegex = new Regex(raw, RegexOptions.IgnoreCase);
        }
    }

    public bool Matches(string typeFull, string methodName) =>
        _typeRegex.IsMatch(typeFull) && _methodRegex.IsMatch(methodName);
}

// ======================================================================== focus writer
internal sealed class FocusWriter
{
    private readonly Dictionary<string, StreamWriter> _writers = new(StringComparer.OrdinalIgnoreCase);

    public FocusWriter(string outDir)
    {
        OutDir = outDir;
    }

    public string OutDir { get; }

    public void Add(string file, string line)
    {
        if (!_writers.TryGetValue(file, out StreamWriter? w))
        {
            w = new StreamWriter(Path.Combine(OutDir, file), false, Program.Utf8NoBom);
            w.WriteLine($"# generated by tools/api-probe — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            _writers[file] = w;
        }
        w.WriteLine(line);
    }

    public void Flush()
    {
        foreach (var kv in _writers) kv.Value.Dispose();
        _writers.Clear();
    }
}

// ======================================================================== type names
internal static class Names
{
    public static bool MatchesAny(string text, string[] regexes) =>
        regexes.Any(r => Regex.IsMatch(text, r));

    public static string TypeFullName(MetadataReader md, TypeDefinitionHandle handle)
    {
        TypeDefinition td = md.GetTypeDefinition(handle);
        string name = md.GetString(td.Name);
        TypeDefinitionHandle decl = td.GetDeclaringType();
        if (!decl.IsNil) return TypeFullName(md, decl) + "+" + name;
        string ns = md.GetString(td.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    public static string TypeRefFullName(MetadataReader md, TypeReferenceHandle handle)
    {
        TypeReference tr = md.GetTypeReference(handle);
        string name = md.GetString(tr.Name);
        EntityHandle scope = tr.ResolutionScope;
        if (scope.Kind == HandleKind.TypeReference)
            return TypeRefFullName(md, (TypeReferenceHandle)scope) + "+" + name;
        string ns = md.GetString(tr.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    public static string HandleTypeName(MetadataReader md, EntityHandle handle)
    {
        if (handle.IsNil) return "<none>";
        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition: return TypeFullName(md, (TypeDefinitionHandle)handle);
            case HandleKind.TypeReference: return TypeRefFullName(md, (TypeReferenceHandle)handle);
            case HandleKind.TypeSpecification: return "<typespec>";
            default: return handle.Kind.ToString();
        }
    }

    public static string Visibility(TypeAttributes a) => (a & TypeAttributes.VisibilityMask) switch
    {
        TypeAttributes.Public => "public",
        TypeAttributes.NotPublic => "internal",
        TypeAttributes.NestedPublic => "public(nested)",
        TypeAttributes.NestedPrivate => "private(nested)",
        TypeAttributes.NestedFamily => "protected(nested)",
        TypeAttributes.NestedAssembly => "internal(nested)",
        TypeAttributes.NestedFamANDAssem => "private-protected(nested)",
        TypeAttributes.NestedFamORAssem => "protected-internal(nested)",
        _ => "?",
    };

    public static string TypeKind(MetadataReader md, TypeDefinition td)
    {
        if ((td.Attributes & TypeAttributes.Interface) != 0) return "interface";
        string baseName = HandleTypeName(md, td.BaseType);
        if (baseName == "System.Enum") return "enum";
        if (baseName == "System.ValueType") return "struct";
        if (baseName == "System.MulticastDelegate") return "delegate";
        return "class";
    }

    public static string MethodVisibility(MethodAttributes a) => (a & MethodAttributes.MemberAccessMask) switch
    {
        MethodAttributes.Private => "private",
        MethodAttributes.FamANDAssem => "private-protected",
        MethodAttributes.Assembly => "internal",
        MethodAttributes.Family => "protected",
        MethodAttributes.FamORAssem => "protected-internal",
        MethodAttributes.Public => "public",
        _ => "?",
    };

    public static string FieldVisibility(FieldAttributes a) => (a & FieldAttributes.FieldAccessMask) switch
    {
        FieldAttributes.Private => "private",
        FieldAttributes.FamANDAssem => "private-protected",
        FieldAttributes.Assembly => "internal",
        FieldAttributes.Family => "protected",
        FieldAttributes.FamORAssem => "protected-internal",
        FieldAttributes.Public => "public",
        _ => "?",
    };
}

// ======================================================================== custom attributes
internal static class Attributes
{
    public static string Describe(MetadataReader md, TypeProvider tp, CustomAttributeHandle handle)
    {
        CustomAttribute ca = md.GetCustomAttribute(handle);
        string ctorType = AttributeTypeName(md, ca.Constructor);
        string shortName = ctorType.EndsWith("Attribute", StringComparison.Ordinal) ? ctorType : ctorType + "Attribute";
        try
        {
            CustomAttributeValue<string> value = ca.DecodeValue(tp);
            var args = new List<string>();
            foreach (CustomAttributeTypedArgument<string> a in value.FixedArguments)
                args.Add(FormatArg(a));
            foreach (CustomAttributeNamedArgument<string> a in value.NamedArguments)
                args.Add($"{a.Name}={FormatValue(a.Value)}");
            return $"{shortName}({string.Join(", ", args)})";
        }
        catch (Exception ex)
        {
            return $"{shortName}(<decode failed: {ex.GetType().Name}: {ex.Message}>)";
        }
    }

    /// <summary>Short flags: only the attributes that change how a mod may call the member.</summary>
    public static string ShortFlags(MetadataReader md, TypeProvider tp, CustomAttributeHandleCollection handles)
    {
        var flags = new List<string>();
        foreach (CustomAttributeHandle h in handles)
        {
            string name = AttributeTypeName(md, md.GetCustomAttribute(h).Constructor);
            switch (name)
            {
                case "System.Runtime.CompilerServices.ExtensionAttribute": flags.Add("extension"); break;
                case "System.ObsoleteAttribute": flags.Add("obsolete"); break;
                case "System.Runtime.CompilerServices.CompilerGeneratedAttribute": flags.Add("compiler-generated"); break;
                case "System.Runtime.InteropServices.DllImportAttribute": flags.Add("DllImport"); break;
            }
        }
        return flags.Count == 0 ? "" : "  [" + string.Join(", ", flags) + "]";
    }

    public static string AttributeTypeName(MetadataReader md, EntityHandle ctor)
    {
        if (ctor.Kind == HandleKind.MemberReference)
        {
            MemberReference mr = md.GetMemberReference((MemberReferenceHandle)ctor);
            return Names.HandleTypeName(md, mr.Parent);
        }
        if (ctor.Kind == HandleKind.MethodDefinition)
            return Names.TypeFullName(md, md.GetMethodDefinition((MethodDefinitionHandle)ctor).GetDeclaringType());
        return ctor.Kind.ToString();
    }

    private static string FormatArg(CustomAttributeTypedArgument<string> a) => FormatValue(a.Value);

    private static string FormatValue(object? value)
    {
        if (value is ImmutableArray<CustomAttributeTypedArgument<string>> arr)
            return "[" + string.Join(", ", arr.Select(FormatArg)) + "]";
        return value switch
        {
            null => "null",
            string s => Program.Escape(s),
            bool b => b ? "true" : "false",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "?",
        };
    }
}

// ======================================================================== literal constants
/// <summary>Decodes a field's compile-time constant (const + enum member values).</summary>
internal static class Constants
{
    public static string? FieldConstant(MetadataReader md, FieldDefinition fd) => Constant(md, fd.GetDefaultValue());

    public static string? ParameterConstant(MetadataReader md, Parameter p) =>
        (p.Attributes & ParameterAttributes.HasDefault) != 0 ? Constant(md, p.GetDefaultValue()) : null;

    public static string? Constant(MetadataReader md, ConstantHandle ch)
    {
        if (ch.IsNil) return null;
        Constant c = md.GetConstant(ch);
        byte[] b = md.GetBlobBytes(c.Value);
        switch (c.TypeCode)
        {
            case ConstantTypeCode.String:
                return Program.Escape(Encoding.Unicode.GetString(b));
            case ConstantTypeCode.Boolean:
                return b.Length > 0 && b[0] != 0 ? "true" : "false";
            case ConstantTypeCode.Char:
                return Program.Escape(((char)BitConverter.ToUInt16(b, 0)).ToString());
            case ConstantTypeCode.SByte:
                return ((sbyte)b[0]).ToString(CultureInfo.InvariantCulture);
            case ConstantTypeCode.Byte:
                return b[0].ToString(CultureInfo.InvariantCulture);
            case ConstantTypeCode.Int16:
                return BitConverter.ToInt16(b, 0).ToString(CultureInfo.InvariantCulture);
            case ConstantTypeCode.UInt16:
                return BitConverter.ToUInt16(b, 0).ToString(CultureInfo.InvariantCulture);
            case ConstantTypeCode.Int32:
                return BitConverter.ToInt32(b, 0).ToString(CultureInfo.InvariantCulture);
            case ConstantTypeCode.UInt32:
                return BitConverter.ToUInt32(b, 0).ToString(CultureInfo.InvariantCulture);
            case ConstantTypeCode.Int64:
                return BitConverter.ToInt64(b, 0).ToString(CultureInfo.InvariantCulture);
            case ConstantTypeCode.UInt64:
                return BitConverter.ToUInt64(b, 0).ToString(CultureInfo.InvariantCulture);
            case ConstantTypeCode.Single:
                return BitConverter.ToSingle(b, 0).ToString("R", CultureInfo.InvariantCulture);
            case ConstantTypeCode.Double:
                return BitConverter.ToDouble(b, 0).ToString("R", CultureInfo.InvariantCulture);
            case ConstantTypeCode.NullReference:
                return "null";
            default:
                return $"<{c.TypeCode}>";
        }
    }
}

// ======================================================================== signature type provider
internal sealed class TypeProvider : ISignatureTypeProvider<string, object?>, ICustomAttributeTypeProvider<string>
{
    private readonly MetadataReader _md;

    public TypeProvider(MetadataReader md) => _md = md;

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Boolean => "bool",
        PrimitiveTypeCode.Byte => "byte",
        PrimitiveTypeCode.SByte => "sbyte",
        PrimitiveTypeCode.Char => "char",
        PrimitiveTypeCode.Int16 => "short",
        PrimitiveTypeCode.UInt16 => "ushort",
        PrimitiveTypeCode.Int32 => "int",
        PrimitiveTypeCode.UInt32 => "uint",
        PrimitiveTypeCode.Int64 => "long",
        PrimitiveTypeCode.UInt64 => "ulong",
        PrimitiveTypeCode.Single => "float",
        PrimitiveTypeCode.Double => "double",
        PrimitiveTypeCode.String => "string",
        PrimitiveTypeCode.Object => "object",
        PrimitiveTypeCode.IntPtr => "nint",
        PrimitiveTypeCode.UIntPtr => "nuint",
        PrimitiveTypeCode.TypedReference => "TypedReference",
        PrimitiveTypeCode.Void => "void",
        _ => typeCode.ToString(),
    };

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind = 0) =>
        Names.TypeFullName(reader, handle);

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind = 0) =>
        Names.TypeRefFullName(reader, handle);

    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind = 0) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public string GetSZArrayType(string elementType) => elementType + "[]";

    public string GetArrayType(string elementType, ArrayShape shape) =>
        elementType + "[" + new string(',', Math.Max(0, shape.Rank - 1)) + "]";

    public string GetByReferenceType(string elementType) => elementType + "&";

    public string GetPointerType(string elementType) => elementType + "*";

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
        genericType + "<" + string.Join(", ", typeArguments) + ">";

    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;

    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) =>
        unmodifiedType + (isRequired ? " modreq(" : " modopt(") + modifier + ")";

    public string GetPinnedType(string elementType) => elementType + " pinned";

    public string GetFunctionPointerType(MethodSignature<string> signature) =>
        "fnptr " + signature.ReturnType + " *(" + string.Join(", ", signature.ParameterTypes) + ")";

    // ICustomAttributeTypeProvider
    public string GetSystemType() => "System.Type";

    public bool IsSystemType(string type) => type == "System.Type";

    public string GetTypeFromSerializedName(string name) => name;

    public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;
}

// ======================================================================== IL decoder
internal enum IlKind
{
    Normal,
    UserString,
}

internal sealed record IlInstruction(int Offset, string OpName, string Operand, IlKind Kind)
{
    /// <summary>Operand text (for ldstr this is the quoted/escaped user string).</summary>
    public string Text => Operand;

    public string Render() => Operand.Length == 0 ? $"IL_{Offset:X4}: {OpName}" : $"IL_{Offset:X4}: {OpName} {Operand}";
}

internal static class IlDecoder
{
    private static readonly Dictionary<short, OpCode> Ops = BuildOps();

    private static Dictionary<short, OpCode> BuildOps()
    {
        var d = new Dictionary<short, OpCode>();
        foreach (FieldInfo f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (f.FieldType != typeof(OpCode)) continue;
            OpCode op = (OpCode)f.GetValue(null)!;
            short key = op.Size == 2 ? unchecked((short)(0xFE00 | (op.Value & 0xFF))) : op.Value;
            d[key] = op;
        }
        return d;
    }

    public static List<IlInstruction> Decode(MetadataReader md, TypeProvider tp, byte[] il)
    {
        var list = new List<IlInstruction>();
        int pos = 0;
        while (pos < il.Length)
        {
            int offset = pos;
            short key = il[pos++];
            if (key == 0xFE && pos < il.Length)
                key = unchecked((short)(0xFE00 | il[pos++]));

            if (!Ops.TryGetValue(key, out OpCode op))
            {
                list.Add(new IlInstruction(offset, $"<unknown opcode 0x{key:X4}>", "", IlKind.Normal));
                break;
            }

            (int size, string operand, IlKind kind) = ReadOperand(md, tp, il, pos, offset, op);
            pos += size;
            list.Add(new IlInstruction(offset, op.Name ?? "", operand, kind));
        }
        return list;
    }

    private static (int Size, string Text, IlKind Kind) ReadOperand(
        MetadataReader md, TypeProvider tp, byte[] il, int pos, int instrOffset, OpCode op)
    {
        switch (op.OperandType)
        {
            case OperandType.InlineNone:
                return (0, "", IlKind.Normal);
            case OperandType.ShortInlineVar:
                return (1, il[pos].ToString(CultureInfo.InvariantCulture), IlKind.Normal);
            case OperandType.InlineVar:
                return (2, ReadUInt16(il, pos).ToString(CultureInfo.InvariantCulture), IlKind.Normal);
            case OperandType.ShortInlineI:
                return (1, ((sbyte)il[pos]).ToString(CultureInfo.InvariantCulture), IlKind.Normal);
            case OperandType.InlineI:
                return (4, ReadInt32(il, pos).ToString(CultureInfo.InvariantCulture), IlKind.Normal);
            case OperandType.InlineI8:
                return (8, ReadInt64(il, pos).ToString(CultureInfo.InvariantCulture), IlKind.Normal);
            case OperandType.ShortInlineR:
                return (4, ReadSingle(il, pos).ToString("R", CultureInfo.InvariantCulture), IlKind.Normal);
            case OperandType.InlineR:
                return (8, ReadDouble(il, pos).ToString("R", CultureInfo.InvariantCulture), IlKind.Normal);
            case OperandType.ShortInlineBrTarget:
            {
                int delta = (sbyte)il[pos];
                int next = pos + 1;
                return (1, $"IL_{next + delta:X4}", IlKind.Normal);
            }
            case OperandType.InlineBrTarget:
            {
                int delta = ReadInt32(il, pos);
                int next = pos + 4;
                return (4, $"IL_{next + delta:X4}", IlKind.Normal);
            }
            case OperandType.InlineSwitch:
            {
                int count = ReadInt32(il, pos);
                var sb = new StringBuilder("(");
                int basePos = pos + 4 + 4 * count;
                for (int i = 0; i < count; i++)
                {
                    int delta = ReadInt32(il, pos + 4 + 4 * i);
                    if (i > 0) sb.Append(", ");
                    sb.Append($"IL_{basePos + delta:X4}");
                }
                sb.Append(')');
                return (4 + 4 * count, sb.ToString(), IlKind.Normal);
            }
            case OperandType.InlineString:
            {
                int token = ReadInt32(il, pos);
                try
                {
                    UserStringHandle h = MetadataTokens.UserStringHandle(token);
                    string s = md.GetUserString(h);
                    return (4, Program.Escape(s), IlKind.UserString);
                }
                catch
                {
                    return (4, $"<bad string token 0x{token:X8}>", IlKind.Normal);
                }
            }
            case OperandType.InlineField:
            case OperandType.InlineMethod:
            case OperandType.InlineType:
            case OperandType.InlineTok:
            {
                int token = ReadInt32(il, pos);
                return (4, FormatToken(md, tp, token), IlKind.Normal);
            }
            case OperandType.InlineSig:
            {
                int token = ReadInt32(il, pos);
                return (4, $"<sig 0x{token:X8}>", IlKind.Normal);
            }
            default:
                return (0, $"<unhandled operand {op.OperandType}>", IlKind.Normal);
        }
    }

    private static string FormatToken(MetadataReader md, TypeProvider tp, int token)
    {
        try
        {
            EntityHandle handle = MetadataTokens.EntityHandle(token);
            switch (handle.Kind)
            {
                case HandleKind.TypeDefinition:
                case HandleKind.TypeReference:
                    return Names.HandleTypeName(md, handle);
                case HandleKind.TypeSpecification:
                    return md.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(tp, null);
                case HandleKind.MethodDefinition:
                {
                    MethodDefinition m = md.GetMethodDefinition((MethodDefinitionHandle)handle);
                    return Names.TypeFullName(md, m.GetDeclaringType()) + "::" + md.GetString(m.Name) + DescribeMethodSig(md, tp, m);
                }
                case HandleKind.FieldDefinition:
                {
                    FieldDefinition f = md.GetFieldDefinition((FieldDefinitionHandle)handle);
                    return Names.TypeFullName(md, f.GetDeclaringType()) + "::" + md.GetString(f.Name);
                }
                case HandleKind.MemberReference:
                    return DescribeMemberRef(md, tp, (MemberReferenceHandle)handle);
                case HandleKind.MethodSpecification:
                {
                    MethodSpecification ms = md.GetMethodSpecification((MethodSpecificationHandle)handle);
                    return FormatToken(md, tp, MetadataTokens.GetToken(ms.Method));
                }
                default:
                    return $"{handle.Kind} 0x{token:X8}";
            }
        }
        catch (Exception ex)
        {
            return $"<bad token 0x{token:X8}: {ex.GetType().Name}>";
        }
    }

    private static string DescribeMethodSig(MetadataReader md, TypeProvider tp, MethodDefinition m)
    {
        MethodSignature<string> sig = m.DecodeSignature(tp, null);
        return "(" + string.Join(", ", sig.ParameterTypes) + ") : " + sig.ReturnType;
    }

    private static string DescribeMemberRef(MetadataReader md, TypeProvider tp, MemberReferenceHandle handle)
    {
        MemberReference mr = md.GetMemberReference(handle);
        string parent = Names.HandleTypeName(md, mr.Parent);
        string name = md.GetString(mr.Name);
        if (mr.GetKind() == MemberReferenceKind.Method)
        {
            MethodSignature<string> sig = mr.DecodeMethodSignature(tp, null);
            return parent + "::" + name + "(" + string.Join(", ", sig.ParameterTypes) + ") : " + sig.ReturnType;
        }
        string fieldType = mr.DecodeFieldSignature(tp, null);
        return parent + "::" + name + " : " + fieldType;
    }

    private static ushort ReadUInt16(byte[] b, int p) => (ushort)(b[p] | b[p + 1] << 8);

    private static int ReadInt32(byte[] b, int p) => b[p] | b[p + 1] << 8 | b[p + 2] << 16 | b[p + 3] << 24;

    private static long ReadInt64(byte[] b, int p)
    {
        ulong lo = (uint)ReadInt32(b, p);
        ulong hi = (uint)ReadInt32(b, p + 4);
        return unchecked((long)(lo | hi << 32));
    }

    private static float ReadSingle(byte[] b, int p) => BitConverter.Int32BitsToSingle(ReadInt32(b, p));

    private static double ReadDouble(byte[] b, int p) => BitConverter.Int64BitsToDouble(ReadInt64(b, p));
}
