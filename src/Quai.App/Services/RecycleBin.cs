using Microsoft.VisualBasic.FileIO;
using Quai.Core.Services;

namespace Quai.App.Services;

/// <summary>Suppression locale via la corbeille de Windows (réversible).</summary>
internal sealed class RecycleBin : ILocalRecycler
{
    public void Recycle(string path)
    {
        if (Directory.Exists(path))
        {
            FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
        else if (File.Exists(path))
        {
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
    }
}
