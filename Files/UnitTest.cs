using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CG.Framework;
using CG.Framework.Engines.Models;
using CG.Framework.Engines.UnrealEngine;
using CG.Framework.Helper;
using CG.Framework.Plugin.Language;

namespace CG.Language.Files;

public class UnitTest : IncludeFile<UnrealCpp>
{
    public override string FileName { get; } = "UnitTest.cpp";
    public override bool IncludeInMainSdkFile { get; } = false;

    public UnitTest(UnrealCpp lang) : base(lang) { }

    public override async ValueTask<string> ProcessAsync(LangProps processProps)
    {
        if (Lang.SdkFile is not UnrealSdkFile sdkFile)
            throw new InvalidOperationException("Invalid language target.");

        // Read File
        string fileStr = await CGUtils.ReadEmbeddedFileAsync(Path.Combine("Internal", FileName), this.GetType().Assembly).ConfigureAwait(false);

        // CLASSES_ASSERT
        const string unitTemplate = @"
		// {0}
		TEST_METHOD({1})
		{
			// Variables
{2}
			// Size
			CHEAT_GEAR_CHECK_SIZE({3}, {4});
		}";

        List<string> GenTestString(IEnumerable<EngineStruct> ss)
        {
            return ss.Select(c =>
                {
                    string cheatGearClassName = $"{sdkFile.Namespace}::{c.NameCpp}";
                    string[] memberTests = c.Variables
                        .Where(m => !m.Static && !m.IsBitField)
                        .Select(m => $"\t\t\tCHEAT_GEAR_CHECK_OFFSET({{3}}, {m.Name.Split('[')[0].Split(':')[0].Trim()}, 0x{m.Offset:X4});")
                        .ToArray();

                    return unitTemplate
                        .Replace("{0}", c.FullName)
                        .Replace("{1}", c.FullName.Replace(" ", "__").Replace(".", "__").Replace("-", "_"))
                        .Replace("{2}", string.Join(Environment.NewLine, memberTests))
                        .Replace("{3}", cheatGearClassName)
                        .Replace("{4}", $"0x{c.Size:X4}");
                })
                .ToList();
        }

        List<string> classAssertStr = GenTestString(Lang.SavedClasses);
        List<string> structsAssertStr = GenTestString(Lang.SavedStructs);

        var fullAssertStr = new List<string>();
        fullAssertStr.AddRange(classAssertStr);
        fullAssertStr.AddRange(structsAssertStr);

        fileStr.Replace("/*!!CLASSES_ASSERT!!*/", string.Join(Environment.NewLine, fullAssertStr));

        return fileStr;
    }
}
