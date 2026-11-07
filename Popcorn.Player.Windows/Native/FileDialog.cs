using System.Runtime.InteropServices;

namespace Popcorn.Player.Windows.Native;

/// <summary>The common Open dialog (comdlg32), on a thread of its own: it runs a modal loop and wants an STA.</summary>
public static class FileDialog
{
    private const int OfnNoChangeDir = 0x8;
    private const int OfnPathMustExist = 0x800;
    private const int OfnFileMustExist = 0x1000;
    private const int OfnExplorer = 0x80000;
    private const int MaxPath = 32768;

    /// <param name="filter">Pairs of description and pattern list, e.g. ("Subtitles", "*.srt;*.ass").</param>
    /// <returns>The picked file, null when cancelled.</returns>
    public static Task<string?> OpenAsync(nint owner, string title, params (string Name, string Patterns)[] filter)
    {
        var result = new TaskCompletionSource<string?>();
        var thread = new Thread(() =>
        {
            try { result.SetResult(Open(owner, title, filter)); }
            catch (Exception ex) { result.SetException(ex); }
        }) { IsBackground = true, Name = "file-dialog" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return result.Task;
    }

    private static string? Open(nint owner, string title, (string Name, string Patterns)[] filter)
    {
        var buffer = Marshal.AllocHGlobal(MaxPath * sizeof(char));
        try
        {
            Marshal.WriteInt16(buffer, 0);
            var dialog = new OpenFileName
            {
                StructSize = Marshal.SizeOf<OpenFileName>(),
                Owner = owner,
                Filter = string.Concat(filter.Select(f => $"{f.Name} ({f.Patterns})\0{f.Patterns}\0")) + "\0",
                File = buffer,
                MaxFile = MaxPath,
                Title = title,
                Flags = OfnExplorer | OfnFileMustExist | OfnPathMustExist | OfnNoChangeDir,
            };
            return GetOpenFileNameW(ref dialog) ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int StructSize;
        public nint Owner;
        public nint Instance;
        public string Filter;
        public nint CustomFilter;
        public int MaxCustomFilter;
        public int FilterIndex;
        public nint File;
        public int MaxFile;
        public nint FileTitle;
        public int MaxFileTitle;
        public string? InitialDir;
        public string Title;
        public int Flags;
        public short FileOffset;
        public short FileExtension;
        public string? DefExt;
        public nint CustData;
        public nint Hook;
        public nint TemplateName;
        public nint Reserved;
        public int ReservedFlags;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetOpenFileNameW(ref OpenFileName dialog);
}
