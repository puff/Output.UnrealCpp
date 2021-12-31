﻿using System.Collections.Generic;
using System.Linq;
using System.Text;
using CG.Framework.Engines.Models;
using LangPrint.Cpp;

namespace CG.Language.Helper;

public static class LangPrintHelper
{
    /// <summary>
    /// Convert <see cref="EngineEnum"/> to <see cref="CppEnum"/>
    /// </summary>
    /// <param name="eEnum">Enum to convert</param>
    /// <returns>Converted <see cref="CppStruct"/></returns>
    internal static CppEnum ToCpp(this EngineEnum eEnum)
    {
        return new CppEnum()
        {
            Name = eEnum.Name,
            Type = eEnum.Type,
            IsClass = true,
            Values = eEnum.Values.Select(kv => new CppNameValue() { Name = kv.Key, Value = kv.Value }).ToList(),
            Conditions = eEnum.Conditions,
        }
        .WithComment(new List<string>() { eEnum.FullName });
    }

    /// <summary>
    /// Convert <see cref="EngineConstant"/> to <see cref="CppConstant"/>
    /// </summary>
    /// <param name="constant">Constant to convert</param>
    /// <returns>Converted <see cref="CppConstant"/></returns>
    internal static CppConstant ToCpp(this EngineConstant constant)
    {
        return new CppConstant()
        {
            Name = constant.Name,
            Type = constant.Type,
            Value = constant.Value,
            Conditions = constant.Conditions,
        };
    }

    /// <summary>
    /// Convert <see cref="EngineVariable"/> to <see cref="CppVariable"/>
    /// </summary>
    /// <param name="variable">Variable to convert</param>
    /// <returns>Converted <see cref="CppVariable"/></returns>
    internal static CppVariable ToCpp(this EngineVariable variable)
    {
        var inlineComment = new StringBuilder();
        inlineComment.Append($"0x{variable.Offset:X4}(0x{variable.Size:X4})");

        if (!string.IsNullOrEmpty(variable.Comment))
            inlineComment.Append($" {variable.Comment}");

        if (!string.IsNullOrEmpty(variable.FlagsString))
            inlineComment.Append($" {variable.FlagsString}");

        return new CppVariable()
        {
            Name = variable.Name,
            Type = variable.Type,
            Value = variable.Value,
            ArrayDim = variable.ArrayDim,
            Bitfield = variable.Bitfield,
            Private = variable.Private,
            Static = variable.Static,
            Const = variable.Const,
            Constexpr = variable.Constexpr,
            Friend = variable.Friend,
            Extern = false,
            Union = variable.Union,
            Conditions = variable.Conditions
        }
        .WithInlineComment(inlineComment.ToString());
    }

    /// <summary>
    /// Convert <see cref="EngineParameter"/> to <see cref="CppParameter"/>
    /// </summary>
    /// <param name="param">Parameter to convert</param>
    /// <returns>Converted <see cref="CppParameter"/></returns>
    internal static CppParameter ToCpp(this EngineParameter param)
    {
        return new CppParameter()
        {
            Name = param.Name,
            Type = (param.PassByReference ? "const " : "") + param.Type + (param.PassByReference ? "&" : (param.ParamKind == FuncParameterKind.Out ? "*" : "")),
            Conditions = param.Conditions
        };
    }

    /// <summary>
    /// Convert <see cref="EngineFunction"/> to <see cref="CppFunction"/>
    /// </summary>
    /// <param name="func">Function to convert</param>
    /// <returns>Converted <see cref="CppStruct"/></returns>
    internal static CppFunction ToCpp(this EngineFunction func)
    {
        List<EngineParameter> @params = func.Parameters
            .Where(p => p.ParamKind != FuncParameterKind.Return && !(p.Name.StartsWith("UnknownData_") && p.Type == "unsigned char"))
            .ToList();

        var comments = new List<string>()
        {
            "Function:",
            $"\t\tOffset -> 0x{func.NativeOffset:X8}",
            $"\t\tName   -> {func.FullName}",
            $"\t\tFlags  -> ({func.FlagsString})",
        };

        if (@params.Count > 0)
            comments.Add("Parameters:");

        foreach (EngineParameter param in @params)
        {
            string comment = string.IsNullOrWhiteSpace(param.FlagsString)
                ? $"\t\t{param.Type,-50} {param.Name}"
                : $"\t\t{param.Type,-50} {param.Name,-58} ({param.FlagsString})";

            comments.Add(comment);
        }

        bool isStatic = func.Static;
        if (func.Static && !func.Predefined && func.Name.StartsWith("STATIC_"))
            isStatic = false;

        return new CppFunction()
        {
            Name = func.Name,
            Type = func.ReturnType,
            TemplateParams = func.TemplateParams,
            Params = @params.Select(ep => ep.ToCpp()).ToList(),
            Body = func.Body,
            Private = func.Private,
            Static = isStatic,
            Const = func.Const,
            Friend = func.Friend,
            Inline = func.Inline,
            Conditions = func.Conditions,
        }
        .WithComment(comments);
    }

    /// <summary>
    /// Convert <see cref="EngineStruct"/> to <see cref="CppStruct"/>
    /// </summary>
    /// <param name="struct">Struct to convert</param>
    /// <returns>Converted <see cref="CppStruct"/></returns>
    internal static CppStruct ToCpp(this EngineStruct @struct)
    {
        string sizeInfo = @struct.InheritedSize > 0
                ? $"Size -> 0x{@struct.Size - @struct.InheritedSize:X4} (FullSize[0x{@struct.Size:X4}] - InheritedSize[0x{@struct.InheritedSize:X4}])"
                : $"Size -> 0x{@struct.Size:X4}";

        return new CppStruct()
        {
            Name = @struct.NameCpp,
            IsClass = false,
            Supers = @struct.Supers.Select(kv => kv.Value).ToList(),
            Variables = @struct.Variables.Select(ev => ev.ToCpp()).ToList(),
            Methods = @struct.Methods.Select(em => em.ToCpp()).ToList(),
            TemplateParams = @struct.TemplateParams,
            Friends = @struct.Friends,
            Conditions = @struct.Conditions,
        }
        .WithComment(new List<string>() { @struct.FullName, sizeInfo });
    }

    /// <summary>
    /// Convert <see cref="EngineClass"/> to <see cref="CppStruct"/>
    /// </summary>
    /// <param name="class">Class to convert</param>
    /// <returns>Converted <see cref="CppStruct"/></returns>
    internal static CppStruct ToCpp(this EngineClass @class)
    {
        CppStruct ret = ((EngineStruct)@class).ToCpp();
        ret.IsClass = true;

        return ret;
    }

    internal static T WithComment<T>(this T cppItem, List<string> comments) where T : CppItemBase
    {
        cppItem.Comment = comments;
        return cppItem;
    }

    internal static T WithInlineComment<T>(this T cppItem, string inlineComment) where T : CppItemBase
    {
        cppItem.InlineComment = inlineComment;
        return cppItem;
    }

    internal static T WithCondition<T>(this T cppItem, List<string> conditions) where T : CppItemBase
    {
        cppItem.Conditions = conditions;
        return cppItem;
    }
}
