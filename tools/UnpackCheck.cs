// Verifies GeckoEngine's unpack path without shipping a 140 MB binary: this
// harness compiles the real Gecko sources, embeds a small zip under the same
// manifest resource name, and unpacks it with the production code path.
//
//   csc -nologo -out:UnpackCheck.exe -r:System.Windows.Forms.dll
//       -r:System.Drawing.dll -r:System.IO.Compression.dll
//       -resource:small.zip,MaterialBrowser.gecko.zip
//       GeckoJson.cs GeckoSocket.cs Gecko.cs Native.cs UnpackCheck.cs
//   UnpackCheck.exe <target folder> <source folder>

using System;
using System.IO;

namespace MaterialBrowser
{
    /// <summary>Stands in for the app's own tracer so these sources link on their own.</summary>
    static class Debug
    {
        public static bool Enabled { get { return false; } }
        public static void Log(string message) { Console.WriteLine("    trace: " + message); }
    }
}

static class UnpackCheck
{
    static int failures;

    static void Check(string name, bool ok, string detail = null)
    {
        if (!ok) failures++;
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + name +
                          (ok || detail == null ? "" : "   [" + detail + "]"));
    }

    static int Main(string[] args)
    {
        string folder = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "mb-unpack-check");
        string source = args.Length > 1 ? args[1] : "";

        Check("the exe carries a runtime", MaterialBrowser.GeckoEngine.HasEmbeddedEngine);
        Check("the embedded runtime is not empty", MaterialBrowser.GeckoEngine.EmbeddedEngineSize > 0,
              MaterialBrowser.GeckoEngine.EmbeddedEngineSize + " bytes");

        MaterialBrowser.GeckoEngine.DeleteTree(folder);
        Directory.CreateDirectory(folder);

        int written = MaterialBrowser.GeckoEngine.ExtractEmbeddedTo(folder);
        Check("ExtractEmbeddedTo writes the tree", written > 0, written + " files");

        foreach (string name in new[] { "firefox.exe", "application.ini", "browse\\omni.ja", "xul.dll.sig" })
            Check("unpacked " + name, File.Exists(Path.Combine(folder, name)));

        if (source.Length > 0)
        {
            int identical = 0, different = 0;
            foreach (string path in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                string relative = path.Substring(folder.Length).TrimStart('\\', '/').Replace('/', '\\');
                string original = Path.Combine(source, relative);
                if (!File.Exists(original)) { different++; continue; }
                if (SameBytes(path, original)) identical++; else different++;
            }
            Check("every unpacked file matches the source byte for byte",
                  different == 0 && identical > 0, identical + " identical, " + different + " different");
        }

        MaterialBrowser.GeckoEngine.DeleteTree(folder);
        Check("the check folder is cleaned up", !Directory.Exists(folder));

        Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILED");
        return failures == 0 ? 0 : 1;
    }

    static bool SameBytes(string left, string right)
    {
        var a = new FileInfo(left);
        var b = new FileInfo(right);
        if (a.Length != b.Length) return false;

        using (FileStream one = File.OpenRead(left))
        using (FileStream two = File.OpenRead(right))
        {
            byte[] x = new byte[64 * 1024], y = new byte[64 * 1024];
            int read;
            while ((read = one.Read(x, 0, x.Length)) > 0)
            {
                if (two.Read(y, 0, read) != read) return false;
                for (int i = 0; i < read; i++) if (x[i] != y[i]) return false;
            }
            return one.ReadByte() < 0 && two.ReadByte() < 0;
        }
    }
}