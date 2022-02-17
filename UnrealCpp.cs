using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CG.Framework.Attributes;
using CG.Framework.Engines;
using CG.Framework.Engines.Models;
using CG.Framework.Engines.UnrealEngine;
using CG.Framework.Helper;
using CG.Framework.Plugin.Language;
using CG.Language.Files;
using CG.Language.Helper;
using LangPrint;
using LangPrint.Cpp;

namespace CG.Language;

internal enum CppOptions
{
    None,
    PrecompileSyntax,
    OffsetsOnly,
    LazyFindObject,
    GenerateParametersFile,

    /// <summary>
    /// If this true (default) the objects are referenced by their name.
    /// Otherwise the objects global index will be used.
    /// Warning: The index may change on updates or even on every start of the games.
    /// </summary>
    ShouldUseStrings,

    /// <summary>
    /// If this true (default: false) the strings printed by the generator get surrounded by _xor_(...).
    /// With the XorStr library these strings get xor encrypted at compile time.
    /// </summary>
    ShouldXorStrings,

    XorFuncName,
}

// TODO: Fix UnitTests

[PluginInfo("CorrM", "Unreal Cpp", "Add Cpp syntax support for UnrealEngine")]
public sealed class UnrealCpp : LanguagePlugin<UnrealSdkFile>
{
    private CppProcessor _cppProcessor;

    protected override Dictionary<string, string> LangTypes { get; } = new()
    {
        // SdkVarType       LangType
        { "int64", "int64_t" },
        { "int32", "int32_t" },
        { "int16", "int16_t" },
        { "int8", "int8_t" },

        { "uint64", "uint64_t" },
        { "uint32", "uint32_t" },
        { "uint16", "uint16_t" },
        { "uint8", "uint8_t" }
    };

    internal List<EngineClass> SavedClasses { get; } = new();
    internal List<EngineStruct> SavedStructs { get; } = new();

    public override string LangName => "Cpp";
    public override GameEngine SupportedEngines => GameEngine.UnrealEngine;
    public override LangProps SupportedProps => LangProps.Internal/* | LangProps.External*/;
    public override IReadOnlyDictionary<Enum, LangOption> Options { get; } = new Dictionary<Enum, LangOption>()
    {
        {
            CppOptions.PrecompileSyntax,
            new LangOption(
                "Precompile Syntax",
                LangOptionType.CheckBox,
                "Use precompile headers for most build speed",
                "true"
            )
        },
        {
            CppOptions.OffsetsOnly,
            new LangOption(
                "Offsets Only",
                LangOptionType.CheckBox,
                "Only dump offsets in sdk",
                "false"
            )
        },
        {
            CppOptions.LazyFindObject,
            new LangOption(
                "Lazy Find Object",
                LangOptionType.CheckBox,
                "Lazy assign for UObject::FindObject in SDK methods body",
                "true"
            )
        },
        {
            CppOptions.GenerateParametersFile,
            new LangOption(
                "Generate Parameters File",
                LangOptionType.CheckBox,
                "Should generate function parameters file",
                "true"
            )
        },
        {
            CppOptions.ShouldUseStrings,
            new LangOption(
                "Should Use Strings",
                LangOptionType.CheckBox,
                "Use string to catch function instead if use it's object index",
                "true"
            )
        },
        {
            CppOptions.ShouldXorStrings,
            new LangOption(
                "Should Xor Strings",
                LangOptionType.CheckBox,
                "Use XOR string to handle functions name",
                "false"
            )
        },
        {
            CppOptions.XorFuncName,
            new LangOption(
                "Xor Func Name",
                LangOptionType.TextBox,
                "XOR function name",
                "_xor_"
            )
        },
    };

    private static string MakeValidName(string name)
    {
        name = name.Replace(' ', '_')
            .Replace('?', '_')
            .Replace('+', '_')
            .Replace('-', '_')
            .Replace(':', '_')
            .Replace('/', '_')
            .Replace('^', '_')
            .Replace('(', '_')
            .Replace(')', '_')
            .Replace('[', '_')
            .Replace(']', '_')
            .Replace('<', '_')
            .Replace('>', '_')
            .Replace('&', '_')
            .Replace('.', '_')
            .Replace('#', '_')
            .Replace('\\', '_')
            .Replace('"', '_')
            .Replace('%', '_');

        if (string.IsNullOrEmpty(name))
            return name;

        if (char.IsDigit(name[0]))
            name = '_' + name;

        return name;
    }

    private List<string> BuildMethodBody(EngineStruct @class, EngineFunction function)
    {
        var body = new List<string>();

        // Function init
        {
            string prefix;
            string initBody;
            if (Options[CppOptions.ShouldUseStrings].Value == "true")
            {
                if (Options[CppOptions.LazyFindObject].Value == "true")
                {
                    body.Add("static UFunction* fn = nullptr;");
                    body.Add("if (!fn)");
                    prefix = "\tfn = ";
                }
                else
                {
                    prefix = "static UFunction* fn = ";
                }

                initBody = "UObject::FindObject<UFunction>(";
                initBody += Options[CppOptions.ShouldXorStrings].Value == "true"
                    ? $"{Options[CppOptions.XorFuncName].Value}(\"{function.FullName}\")"
                    : $"\"{function.FullName}\"";
                initBody += $");";
            }
            else
            {
                prefix = "static UFunction* fn = ";
                initBody = $"UObject::GetObjectCasted<UFunction>({function.Index});";
            }

            body.Add($"{prefix}{initBody}");
            body.Add("");
        }

        // Parameters
        {
            if (Options[CppOptions.GenerateParametersFile].Value == "true")
            {
                body.Add($"{@class.NameCpp}_{function.Name}_Params params {{}};");
            }
            else
            {
                body.Add("struct");
                body.Add("{");

                foreach (EngineParameter param in function.Parameters)
                {
                    if (param.Name.StartsWith("UnknownData_") && param.Type == "unsigned char")
                        continue;

                    body.Add($"\t{param.Type,-50} {param.Name};");
                }

                body.Add("} params;");
            }

            List<EngineParameter> retVal = function.Parameters.Where(item => item.ParamKind == FuncParameterKind.Default).ToList();
            if (retVal.Count > 0)
            {
                foreach (EngineParameter param in retVal)
                {
                    // Not needed
                    if (param.Name.StartsWith("UnknownData_") && param.Type == "unsigned char")
                        continue;

                    body.Add($"params.{param.Name} = {param.Name};");
                }
            }
            body.Add("");
        }

        // Function call
        {
            body.Add("auto flags = fn->FunctionFlags;");

            if (function.Native)
                body.Add($"fn->FunctionFlags |= 0x{UnrealFunctionFlags.UE4Native:X};");

            if (function.Static && !SdkFile.ShouldConvertStaticMethods)
            {
                string prefix;
                if (Options[CppOptions.LazyFindObject].Value == "true")
                {
                    body.Add("static UObject* defaultObj = nullptr;");
                    body.Add("if (!defaultObj)");
                    prefix = "\tdefaultObj = ";
                }
                else
                {
                    prefix = "static UObject* defaultObj = ";
                }

                body.Add($"{prefix}StaticClass()->CreateDefaultObject<{@class.NameCpp}>();");
                body.Add("defaultObj->ProcessEvent(fn, &params);");
            }
            else
            {
                body.Add("UObject::ProcessEvent(fn, &params);");
            }

            body.Add("fn->FunctionFlags = flags;");
        }

        // Out Parameters
        {
            List<EngineParameter> rOut = function.Parameters.Where(item => item.ParamKind == FuncParameterKind.Out).ToList();
            if (rOut.Count > 0)
            {
                body.Add("");
                foreach (EngineParameter param in rOut)
                {
                    body.Add($"if ({param.Name} != nullptr)");
                    body.Add($"\t*{param.Name} = params.{param.Name};");
                }
            }
        }

        // Return Value
        {
            List<EngineParameter> ret = function.Parameters.Where(item => item.ParamKind == FuncParameterKind.Return).ToList();
            if (ret.Count > 0)
            {
                body.Add("");
                body.Add($"return params.{ret[0].Name};");
            }
        }

        return body;
    }

    private void AddPredefinedMethodsToStruct(EngineStruct es)
    {
        //    // AfterRead func
        //    if (processProps == LangProps.External && Options[CppOptions.OffsetsOnly].Value == "false")
        //        builder.AppendLine($"\t{BuildReadExternalMethod(c, true)}");
    }

    private void AddPredefinedMethodsToClass(EngineClass ec)
    {
        // StaticClass
        {
            var staticClassFunc = new EngineFunction()
            {
                FullName = $"PredefindFunction {ec.NameCpp}.StaticClass",
                Name = "StaticClass",
                ReturnType = "UClass*",
                Static = true,
                Predefined = true,
                Const = false,
                Inline = false,
                Native = false,
                Private = false,
                FlagsString = "Predefined, Static"
            };

            string prefix;
            if (Options[CppOptions.LazyFindObject].Value == "true")
            {
                staticClassFunc.Body.Add("static UClass* ptr = nullptr;");
                staticClassFunc.Body.Add("if (!ptr)");
                prefix = "\tptr = ";
            }
            else
            {
                prefix = "static UClass* ptr = ";
            }

            if (Options[CppOptions.ShouldUseStrings].Value == "true")
            {
                string classStr = Options[CppOptions.ShouldXorStrings].Value == "true" ? $"{Options[CppOptions.XorFuncName].Value}(\"{ec.FullName}\")" : $"\"{ec.FullName}\"";
                staticClassFunc.Body.Add($"{prefix}UObject::FindClass({classStr});");
            }
            else
            {
                staticClassFunc.Body.Add($"{prefix}UObject::GetObjectCasted<UClass>({ec.ObjectIndex});");
            }

            staticClassFunc.Body.Add($"return ptr;");

            ec.Methods.Add(staticClassFunc);
        }
    }

    private string BuildReadExternalMethod(EngineStruct @class, bool inHeader)
    {
        //var afterReadMethod = new EngineFunction() { Name = "AfterRead", ReturnType = "void" };
        //var beforeDeleteMethod = new EngineFunction() { Name = "BeforeDelete", ReturnType = "void" };

        //var afterReadStr = new MyStringBuilder();
        //var beforeDeleteStr = new MyStringBuilder();

        //afterReadStr.AppendLine(BuildMethodSignature(afterReadMethod, @class, inHeader));
        //beforeDeleteStr.AppendLine(BuildMethodSignature(beforeDeleteMethod, @class, inHeader));

        //if (inHeader)
        //    return afterReadStr + "\t" + beforeDeleteStr;

        //afterReadStr.AppendLine("{");
        //beforeDeleteStr.AppendLine("{");

        //if (@class.Supers.Count > 0)
        //{
        //    afterReadStr.AppendLine($"\t{@class.Supers}::{afterReadMethod.Name}();{Environment.NewLine}");
        //    beforeDeleteStr.AppendLine($"\t{@class.Supers}::{beforeDeleteMethod.Name}();{Environment.NewLine}");
        //}

        //foreach (EngineVariable member in @class.Variables)
        //{
        //    // Is not static
        //    if ((member.Status & MemberStatus.Static) != 0)
        //        continue;

        //    // Is pointer
        //    if (member.Type[^1] != '*')
        //        continue;

        //    // Get type of member
        //    string memType = member.Type.Contains(' ') ? member.Type[(member.Type.LastIndexOf(' ') + 1)..^1] : member.Type[..^1];

        //    // If it's just `void*`
        //    if (memType == "void")
        //        continue;

        //    afterReadStr.AppendLine($"\tREAD_PTR_FULL({member.Name}, {memType});");
        //    beforeDeleteStr.AppendLine($"\tDELE_PTR_FULL({member.Name});");
        //}

        //afterReadStr.AppendLine("}");
        //beforeDeleteStr.AppendLine("}");

        //return $"{afterReadStr}{Environment.NewLine}{beforeDeleteStr}";

        return string.Empty;
    }

    private static List<string> GenerateMethodConditions()
    {
        return new List<string>()
        {
            $"!{nameof(CppOptions.OffsetsOnly)}"
        };
    }

    private void PreparePackageModel(CppPackageModel cppPackage, IEnginePackage enginePackage)
    {
        // # Conditions
        if (Options[CppOptions.OffsetsOnly].Value == "true")
            cppPackage.Conditions.Add(nameof(CppOptions.OffsetsOnly));

        if (enginePackage.IsPredefined && enginePackage.Name == "BasicTypes")
        {
            //cppPackage.Pragmas.Add("warning(disable: 4267)");
            // # conditions
            //if (processProps == LangProps.External)
            //    cppPackage.Conditions.Add("EXTERNAL_PROPS");
            //fileStr.Replace("/*!!POINTER_SIZE!!*/", sdkFile.Is64BitGame ? "0x08" : "0x04");
            //fileStr.Replace("/*!!FText_SIZE!!*/", $"0x{Lang.EngineConfig.GetStruct("FText").Size:X}");
            //// Set FUObjectItem_MEMBERS
            //{
            //    List<PredefinedMember> fUObjectItemMembers = Lang.EngineConfig.GetStructAsLangMembers(Lang, "FUObjectItem");
            //    string fUObjectItemStr = string.Join(Environment.NewLine, fUObjectItemMembers.Select(variable => $"\t{variable.Type} {variable.Name};"));
            //    fileStr.Replace("/*!!FUObjectItem_MEMBERS!!*/", fUObjectItemStr);
            //}

            // # Forwards
            cppPackage.Forwards = new List<string>()
            {
                "class UObject"
            };

            CppFunction initFunc = cppPackage.Functions.Find(cf => cf.Name == "InitSdk" && cf.Params.Count == 0);
            if (initFunc is not null)
            {
                for (var i = 0; i < initFunc.Body.Count; i++)
                {
                    string s = initFunc.Body[i]
                        .Replace("MODULE_NAME", SdkFile.GameModule)
                        .Replace("GOBJ_OFFSET", $"0x{SdkFile.GObjectsOffset:X6}")
                        .Replace("GNAME_OFFSET", $"0x{SdkFile.GNamesOffset:X6}")
                        .Replace("GWORLD_OFFSET", $"0x{SdkFile.GWorldOffset:X6}");
                    initFunc.Body[i] = s;
                }
            }
        }
    }

    private void PrepareEngineFunction(EngineStruct parent, EngineFunction eFunc)
    {
        if (!eFunc.Predefined)
            eFunc.Body = BuildMethodBody(parent, eFunc);
    }

    private void PrepareCppFunction(EngineFunction originalFunc, CppFunction cppFunc)
    {
        if (SdkFile.ShouldConvertStaticMethods && originalFunc.Static && !originalFunc.Predefined)
        {
            cppFunc.Name = $"STATIC_{cppFunc.Name}";
            cppFunc.Static = false;
        }
    }

    private CppStruct ConvertStruct(EngineStruct eStruct)
    {
        // Prepare methods
        foreach (EngineFunction eFunc in eStruct.Methods)
            PrepareEngineFunction(eStruct, eFunc);

        // Convert
        CppStruct cppStruct = eStruct.ToCpp();

        if (eStruct is EngineClass)
            cppStruct.IsClass = true;

        foreach (CppFunction cppFunc in cppStruct.Methods)
        {
            cppFunc.Conditions.AddRange(GenerateMethodConditions());
            PrepareCppFunction(eStruct.Methods.Find(m => m.Name == cppFunc.Name), cppFunc);
        }

        return cppStruct;
    }

    private IEnumerable<CppDefine> GetDefines(IEnginePackage enginePackage)
    {
        return enginePackage.Defines.Select(ec => ec.ToCpp());
    }

    private IEnumerable<CppConstant> GetConstants(IEnginePackage enginePackage)
    {
        return enginePackage.Constants.Select(ec => ec.ToCpp());
    }

    private IEnumerable<CppEnum> GetEnums(IEnginePackage enginePackage)
    {
        return enginePackage.Enums.Select(ee => ee.ToCpp());
    }

    private IEnumerable<CppFunction> GetFunctions(IEnginePackage enginePackage)
    {
        return enginePackage.Functions.Select(ef => ef.ToCpp());
    }

    private IEnumerable<CppStruct> GetStructs(IEnginePackage enginePackage)
    {
        return enginePackage.Structs.Select(ConvertStruct);
    }

    private IEnumerable<CppStruct> GetClasses(IEnginePackage enginePackage)
    {
        return enginePackage.Classes.Select(ConvertStruct);
    }

    private IEnumerable<CppStruct> GetFuncParametersStructs(IEnginePackage enginePackage)
    {
        var ret = new List<CppStruct>();
        IEnumerable<(EngineClass, EngineFunction)> functions = enginePackage.Classes
            .SelectMany(@class => @class.Methods.Select(func => (@class, func)))
            .Where(classFunc => !classFunc.func.Predefined);

        foreach ((EngineClass @class, EngineFunction func) in functions)
        {
            var cppParamStruct = new CppStruct()
            {
                Name = $"{@class.NameCpp}_{func.Name}_Params",
                IsClass = false,
                Comments = new List<string>() { func.FullName }
            };

            foreach (EngineParameter param in func.Parameters)
            {
                if (param.Name.StartsWith("UnknownData_") && param.Type == "unsigned char")
                    continue;

                var cppVar = new CppVariable()
                {
                    Type = param.Type,
                    Name = param.Name,
                    InlineComment = $"0x{param.Offset:X4}(0x{param.Size:X4}) {param.Comment} ({param.FlagsString})"
                };

                cppParamStruct.Variables.Add(cppVar);
            }

            ret.Add(cppParamStruct);
        }

        return ret;
    }

    private string MakeFuncParametersFile(CppPackageModel package, IEnumerable<CppStruct> paramStructs)
    {
        var sb = new StringBuilder();
        var pragmas = new List<string>()
        {
            "once"
        };
        var includes = new List<string>();

        if (Options[CppOptions.PrecompileSyntax].Value != "true")
            includes.Add("\"../SDK.h\"");

        // File header
        sb.Append(_cppProcessor.GetFileHeader(package.HeadingComment, package.NameSpace, pragmas, includes, null, package.BeforeNameSpace, out int indentLvl));

        // Structs
        sb.Append(_cppProcessor.GenerateStructs(paramStructs, indentLvl, null));

        // File footer
        sb.Append(_cppProcessor.GetFileFooter(package.NameSpace, package.AfterNameSpace, ref indentLvl));

        return sb.ToString();
    }

    protected override ValueTask OnInitAsync()
    {
        ArgumentNullException.ThrowIfNull(SdkFile);

        _cppProcessor = new CppProcessor();
        var cppOpts = new CppLangOptions()
        {
            NewLine = NewLineType.CRLF,
            PrintSectionName = true,
            InlineCommentPadSize = 56,
            VariableMemberTypePadSize = 60,
            GeneratePackageSyntax = true,
            AddPackageHeaderToCppFile = false
        };
        _cppProcessor.Init(cppOpts);

        SavedClasses.Clear();
        SavedStructs.Clear();

        // Sort structs in packages
        PackageSorter.SortStructsClassesInPackages(SdkFile.Packages);

        // Add predefined methods
        foreach (IEnginePackage pack in SdkFile.Packages.Where(p => !p.IsPredefined))
        {
            foreach (EngineStruct @struct in pack.Structs)
                AddPredefinedMethodsToStruct(@struct);

            foreach (EngineClass @class in pack.Classes)
                AddPredefinedMethodsToClass(@class);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Generate enginePackage files
    /// </summary>
    /// <param name="enginePackage">Package to generate files for</param>
    /// <returns>File name and its content</returns>
    private ValueTask<Dictionary<string, string>> GeneratePackageFilesAsync(IEnginePackage enginePackage)
    {
#if DEBUG
        //if (enginePackage.Name != "InstancesHelper" && enginePackage.Name != "BasicTypes" && enginePackage.Name != "CoreUObject") // BasicTypes
        //    return ValueTask.FromResult(new Dictionary<string, string>());
#endif

        var ret = new Dictionary<string, string>();

        // Make CppPackageModel
        List<CppStruct> structs = GetStructs(enginePackage).ToList();
        structs.AddRange(GetClasses(enginePackage));

        // Make CppPackageModel
        var cppModel = new CppPackageModel()
        {
            Name = enginePackage.Name,
            BeforeNameSpace = $"#ifdef _MSC_VER{Environment.NewLine}\t#pragma pack(push, 0x{SdkFile.GlobalMemberAlignment:X2}){Environment.NewLine}#endif",
            AfterNameSpace = $"#ifdef _MSC_VER{Environment.NewLine}\t#pragma pack(pop){Environment.NewLine}#endif",
            HeadingComment = new List<string>() { $"Name: {SdkFile.GameName}", $"Version: {SdkFile.GameVersion}" },
            NameSpace = SdkFile.Namespace,
            Pragmas = new List<string>() { "once" },
            Defines = GetDefines(enginePackage).ToList(),
            Functions = GetFunctions(enginePackage).ToList(),
            Constants = GetConstants(enginePackage).ToList(),
            Enums = GetEnums(enginePackage).ToList(),
            Structs = structs,
            Conditions = enginePackage.Conditions
        };

        cppModel.CppIncludes.Add(Options[CppOptions.PrecompileSyntax].Value == "true" ? "\"../pch.h\"" : "\"../SDK.h\"");
        PreparePackageModel(cppModel, enginePackage);

        // Parameters Files
        if (!enginePackage.IsPredefined && Options[CppOptions.GenerateParametersFile].Value == "true" && Options[CppOptions.OffsetsOnly].Value != "true")
        {
            string fileName = $"{enginePackage.Name}_Params.h";
            IEnumerable<CppStruct> paramStructs = GetFuncParametersStructs(enginePackage);
            string paramsFile = MakeFuncParametersFile(cppModel, paramStructs);

            cppModel.PackageHeaderIncludes.Add($"\"{fileName}\"");
            ret.Add(Path.Combine("SDK", fileName), paramsFile);
        }

        // Generate files
        Dictionary<string, string> cppFiles = _cppProcessor.GenerateFiles(cppModel)
            .ToDictionary(kv => Path.Combine("SDK", kv.Key), kv => kv.Value);

        foreach ((string fName, string fContent) in cppFiles)
            ret.Add(fName, fContent);

        // Useful for unit tests
        SavedStructs.AddRange(structs.Where(cs => !cs.IsClass).SelectMany(cs => enginePackage.Structs.Where(ec => ec.NameCpp == cs.Name)));
        SavedClasses.AddRange(structs.Where(cs => cs.IsClass).SelectMany(cs => enginePackage.Classes.Where(ec => ec.NameCpp == cs.Name)));

        return ValueTask.FromResult(ret);
    }

    /// <summary>
    /// Process local files that needed to be included
    /// </summary>
    /// <param name="processProps">Process props</param>
    private async ValueTask<Dictionary<string, string>> GenerateIncludesAsync(LangProps processProps)
    {
        var ret = new Dictionary<string, string>();

        // Init
        var unitTestCpp = new UnitTest(this);

        if (processProps == LangProps.External)
        {
            var mmHeader = new MemManagerHeader(this);
            var mmCpp = new MemManagerCpp(this);

            ValueTask<string> taskMmHeader = mmHeader.ProcessAsync(processProps);
            ValueTask<string> taskMmCpp = mmCpp.ProcessAsync(processProps);

            ret.Add(mmHeader.FileName, await taskMmHeader.ConfigureAwait(false));
            ret.Add(mmCpp.FileName, await taskMmCpp.ConfigureAwait(false));
        }

        // Process
        ValueTask<string> taskUnitTestCpp = unitTestCpp.ProcessAsync(processProps);

        // Wait tasks
        ret.Add(unitTestCpp.FileName, await taskUnitTestCpp.ConfigureAwait(false));

        // PchHeader
        if (Options[CppOptions.PrecompileSyntax].Value == "true")
        {
            var pchHeader = new PchHeader(this);
            ret.Add(pchHeader.FileName, await pchHeader.ProcessAsync(processProps).ConfigureAwait(false));
        }

        return ret;
    }

    /// <summary>
    /// Generate file for missed structs
    /// </summary>
    /// <returns>File name and file content</returns>
    private (string, string) GenerateMissing()
    {
        // "MISSING.h"

        // Check for missing structs
        if (SdkFile.MissedStructs.Count == 0)
            return default;

        List<CppStruct> structs = SdkFile.MissedStructs.ConvertAll(ConvertStruct);
        foreach (CppStruct cppStruct in structs)
        {
            EngineStruct es = SdkFile.MissedStructs.Find(es => es.NameCpp == cppStruct.Name);
            if (es is null)
                throw new Exception($"Can't find missing struct '{cppStruct.Name}'");

            cppStruct.Variables.Add(new CppVariable()
            {
                Name = "UnknownData",
                Type = "unsigned char",
                ArrayDim = $"0x{es.Size:X}"
            });
        }

        var sb = new StringBuilder();
        sb.Append(_cppProcessor.GetFileHeader(null, "CG", null, null, null, string.Empty, out int indetLvl));
        sb.Append(_cppProcessor.GenerateStructs(SdkFile.MissedStructs.ConvertAll(ConvertStruct), indetLvl, null));
        sb.Append(_cppProcessor.GetFileFooter("CG", string.Empty, ref indetLvl));

        //var builder = new Dictionary<string, string>();
        //builder.Append(GetFileHeader(true));

        //foreach (EngineStruct s in SdkFileBase.MissedStructs)
        //{
        //    builder.AppendLine($"// {s.FullName}");
        //    builder.AppendLine($"// 0x{s.Size:X4}");

        //    builder.AppendLine($"class {MakeValidName(s.NameCpp)}{Environment.NewLine}{{");
        //    builder.AppendLine($"\tuint8_t UnknownData[0x{s.Size:X}];{Environment.NewLine}}};{Environment.NewLine}");
        //}

        //builder.Append(GetFileFooter());
        return ("MISSING.h", "");
    }

    public override async ValueTask<Dictionary<string, string>> GenerateFilesAsync(LangProps processProps)
    {
        var ret = new Dictionary<string, string>();
        var builder = new MyStringBuilder();

        builder.AppendLine($"#pragma once{Environment.NewLine}");
        builder.AppendLine("// --------------------------------------- \\\\");
        builder.AppendLine("//      Sdk Generated By ( CheatGear )     \\\\");
        builder.AppendLine("// --------------------------------------- \\\\");
        builder.AppendLine($"// Name: {SdkFile.GameName.Trim()}, Version: {SdkFile.GameVersion}{Environment.NewLine}");

        builder.AppendLine("#include <set>");
        builder.AppendLine("#include <string>");
        builder.AppendLine("#include <vector>");
        builder.AppendLine("#include <locale>");
        builder.AppendLine("#include <unordered_set>");
        builder.AppendLine("#include <unordered_map>");
        builder.AppendLine("#include <iostream>");
        builder.AppendLine("#include <sstream>");
        builder.AppendLine("#include <cstdint>");
        builder.AppendLine("#include <Windows.h>");

        // Includes
        foreach ((string fName, string fContent) in await GenerateIncludesAsync(processProps).ConfigureAwait(false))
        {
            ret.Add(fName, fContent);

            if (!fName.EndsWith(".cpp") && fName.ToLower() != "pch.h")
                builder.AppendLine($"#include \"{fName.Replace("\\", "/")}\"");
        }

        // Packages generator
        int packCount = 0;
        foreach (IEnginePackage pack in SdkFile.Packages)
        {
            foreach ((string fName, string fContent) in await GeneratePackageFilesAsync(pack).ConfigureAwait(false))
                ret.Add(fName, fContent);

            if (Status?.ProgressbarStatus is not null)
            {
                await Status.ProgressbarStatus.Invoke(
                    packCount,
                    SdkFile.Packages.Count - packCount,
                    CGUtils.GetPercentage(packCount, SdkFile.Packages.Count, 100)
                ).ConfigureAwait(false);
            }

            packCount++;
        }

        // Missed structs
        if (SdkFile.MissedStructs.Count > 0)
        {
            if (Status?.TextStatus is not null)
                await Status.TextStatus.Invoke("Generating missed structs").ConfigureAwait(false);

            (string fName, string fContent) = GenerateMissing();
            ret.Add(Path.Combine("SDK", fName), fContent);

            builder.AppendLine($"#include \"SDK/{fName}\"");
        }

        builder.Append(Environment.NewLine);

        // Package sorter
        if (Status?.TextStatus is not null)
            await Status.TextStatus.Invoke("Sort packages depend on dependencies").ConfigureAwait(false);

        PackageSorterResult<IEnginePackage> sortResult = PackageSorter.Sort(SdkFile.Packages);
        if (sortResult.CycleList.Count > 0)
        {
            builder.AppendLine("// # Dependency cycle headers");
            builder.AppendLine($"// # (Sorted: {sortResult.SortedList.Count}, Cycle: {sortResult.CycleList.Count})\n");

            foreach ((IEnginePackage package, IEnginePackage dependPackage) in sortResult.CycleList)
            {
                builder.AppendLine($"// {package.Name} <-> {dependPackage.Name}");
                builder.AppendLine($"#include \"SDK/{package.Name}_Package.h\"");
            }

            builder.AppendLine();
            builder.AppendLine();
        }

        foreach (IEnginePackage package in sortResult.SortedList.Where(p => p.IsPredefined))
            builder.AppendLine($"#include \"SDK/{package.Name}_Package.h\"");

        foreach (IEnginePackage package in sortResult.SortedList.Where(p => !p.IsPredefined))
            builder.AppendLine($"#include \"SDK/{package.Name}_Package.h\"");

        ret.Add("SDK.h", builder.ToString());
        return ret;
    }

    // Todo: this not the way to fix it but, i'm lazy so fix that leater :)
    public override string ToLangType(string varType)
    {
        string ret = base.ToLangType(varType);
        ret = ret.Replace("uint8_t_t_t", "uint8_t");
        ret = ret.Replace("uint16_t_t", "uint16_t");

        return ret;
    }
}
