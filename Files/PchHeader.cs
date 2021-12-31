using System;
using System.IO;
using System.Threading.Tasks;
using CG.Framework;
using CG.Framework.Helper;
using CG.Framework.Plugin.Language;

namespace CG.Language.Files;

public class PchHeader : IncludeFile<UnrealCpp>
{
    public override string FileName { get; } = "pch.h";
    public override bool IncludeInMainSdkFile { get; } = false;

    public PchHeader(UnrealCpp lang) : base(lang) { }

    public override async ValueTask<string> ProcessAsync(LangProps processProps)
    {
        if (Lang.SdkFile is null)
            throw new InvalidOperationException("Invalid language target.");

        // Read File
        return await CGUtils.ReadEmbeddedFileAsync(Path.Combine("Internal", FileName), this.GetType().Assembly).AsTask().ConfigureAwait(false);
    }
}
