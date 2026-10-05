// Packs the bundled Gecko runtime into a zip that build.bat embeds into
// MaterialBrowser.exe. The app unpacks it on the first launch, so the exe alone
// is enough to run.
//
//   csc /nologo /out:PackEngine.exe /r:System.IO.Compression.dll \
//       /r:System.IO.Compression.FileSystem.dll PackEngine.cs
//   PackEngine.exe gecko gecko.zip

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;

static class PackEngine
{
    /// <summary>Nothing here is needed to run the browser.</summary>
    static readonly string[] Skip =
    {
        "install.log",
        "maintenanceservice.exe",
        "maintenanceservice_installer.exe",
    };

    static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: PackEngine.exe <folder> <out.zip>");
            return 2;
        }

        string source = Path.GetFullPath(args[0]);
        string target = Path.GetFullPath(args[1]);
        if (!Directory.Exists(source))
        {
            Console.Error.WriteLine("no such folder: " + source);
            return 1;
        }

        var skip = new HashSet<string>(Skip, StringComparer.OrdinalIgnoreCase);
        var timer = Stopwatch.StartNew();

        if (File.Exists(target)) File.Delete(target);
        int files = 0;
        long raw = 0;

        using (FileStream output = File.Create(target, 1 << 20))
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
        {
            foreach (string path in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(path);
                if (skip.Contains(name)) continue;

                string relative = path.Substring(source.Length).TrimStart('\\', '/').Replace('\\', '/');
                ZipArchiveEntry entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(File.GetLastWriteTime(path));

                using (Stream input = File.OpenRead(path))
                using (Stream entryStream = entry.Open())
                {
                    byte[] buffer = new byte[1 << 18];
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        entryStream.Write(buffer, 0, read);
                        raw += read;
                    }
                }
                files++;
            }
        }

        Console.WriteLine("packed {0} files ({1:N0} MB) into {2} ({3:N0} MB) in {4:N1}s",
            files, raw / 1048576.0, Path.GetFileName(target),
            new FileInfo(target).Length / 1048576.0, timer.Elapsed.TotalSeconds);
        return 0;
    }
}