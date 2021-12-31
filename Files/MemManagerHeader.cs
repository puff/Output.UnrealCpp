using System;
using System.IO;
using System.Threading.Tasks;
using CG.Framework;
using CG.Framework.Helper;
using CG.Framework.Plugin.Language;

namespace CG.Language.Files;

public class MemManagerHeader : IncludeFile<UnrealCpp>
{
    public override string FileName { get; } = "MemoryManager.h";
    public override bool IncludeInMainSdkFile { get; } = false;

    public MemManagerHeader(UnrealCpp lang) : base(lang) { }

    public override async ValueTask<string> ProcessAsync(LangProps processProps)
    {
        if (Lang.SdkFile is null)
            throw new InvalidOperationException("Invalid language target.");

        // Read File
        return await CGUtils.ReadEmbeddedFileAsync(Path.Combine("External", FileName), this.GetType().Assembly).ConfigureAwait(false);
    }
}
