
using EnvDTE;
using Microsoft.VisualStudio.OLE.Interop;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Popcorn.PlayerServer;
internal class DebugUtils
{
    public static void AttachProcessToDebugger(int pid)
    {
        // Get an instance of the currently running Visual Studio IDE
        var dte = GetInstances().FirstOrDefault();
        if (dte is null) return;

        foreach (EnvDTE.Process process in dte.Debugger.LocalProcesses)
        {
            if (process.ProcessID == pid)
            {
                process.Attach();
                Debug.WriteLine("Debugger attached to process " + process.Name);
                return;
            }
        }
    }

    private static IEnumerable<DTE> GetInstances()
    {
        IRunningObjectTable rot;
        IEnumMoniker enumMoniker;
        int retVal = GetRunningObjectTable(0, out rot);

        if (retVal == 0)
        {
            rot.EnumRunning(out enumMoniker);

            uint fetched = uint.MinValue;
            IMoniker[] moniker = new IMoniker[1];
            while (enumMoniker.Next(1, moniker, out fetched) == 0)
            {
                IBindCtx bindCtx;
                CreateBindCtx(0, out bindCtx);
                string displayName;
                moniker[0].GetDisplayName(bindCtx, null, out displayName);
                Console.WriteLine("Display Name: {0}", displayName);
                bool isVisualStudio = displayName.StartsWith("!VisualStudio");
                if (isVisualStudio)
                {
                    object obj;
                    rot.GetObject(moniker[0], out obj);
                    var dte = obj as DTE;
                    yield return dte;
                }
            }
        }
    }

    [DllImport("ole32.dll")]
    private static extern void CreateBindCtx(int reserved, out IBindCtx ppbc);

    [DllImport("ole32.dll")]
    private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable prot);
}
