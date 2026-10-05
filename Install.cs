// Installer and uninstaller for MaterialBrowser, in one executable.
//
//   Setup.exe                     install into the default per-user folder
//   Setup.exe -Dir "D:\Apps\MB"   install into a chosen folder
//   Setup.exe -Silent             no questions, no shortcuts on the desktop
//   Uninstall.exe --uninstall     remove the folder this copy lives in
//
// The installer always creates a folder of its own: if the chosen path already
// holds files, it uses a "MaterialBrowser" subfolder there. The uninstaller only
// ever deletes the folder it was started from, and only after finding the
// program inside it.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

static class Setup
{
    const string ProgramName = "MaterialBrowser";
    const string AppExe = "MaterialBrowser.exe";
    const string AppIcon = "MaterialBrowser.ico";
    const string EngineFolder = "gecko";
    const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + ProgramName;

    /// <summary>Files copied next to the program. The engine folder is copied whole.</summary>
    static readonly string[] Files = { AppExe, AppIcon };

    static int Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch { }

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--uninstall") return Uninstall();
            if (args[i] == "--remove")
            {
                // The helper is told which folder to remove and which process to
                // wait for, never where it lives itself.
                if (i + 1 >= args.Length) return 2;
                int parentPid = 0;
                if (i + 2 < args.Length) int.TryParse(args[i + 2], out parentPid);
                return RemoveFolder(args[i + 1], parentPid);
            }
        }

        string target = Value(args, "-Dir");
        bool silent = false;
        foreach (string arg in args) if (arg == "-Silent") silent = true;

        return Install(target, silent);
    }

    static string Value(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return "";
    }

    // ─── install ─────────────────────────────────────────────────
    static int Install(string requested, bool silent)
    {
        string source = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
        if (!File.Exists(Path.Combine(source, AppExe)))
        {
            Fail("Рядом с установщиком нет " + AppExe + ". Запускать нужно из папки программы.");
            return 2;
        }

        string folder = TargetFolder(requested);
        Say("Установка в " + folder);

        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

        foreach (string name in Files)
        {
            string from = Path.Combine(source, name);
            if (!File.Exists(from)) continue;
            CopyFile(from, Path.Combine(folder, name), true);
            Say("  файл  " + name);
        }

        string engine = Path.Combine(source, EngineFolder);
        if (Directory.Exists(engine))
        {
            int files = CopyTree(engine, Path.Combine(folder, EngineFolder));
            Say("  движок  " + files + " файлов");
        }
        else Say("  движок не найден рядом с установщиком, копирую без него");

        // The uninstaller is this very program under its other name.
        string uninstaller = Path.Combine(folder, "Uninstall.exe");
        string me = Process.GetCurrentProcess().MainModule.FileName;
        CopyFile(me, uninstaller, true);
        Say("  деинсталятор  Uninstall.exe");

        Shortcuts(folder, silent);
        Register(folder);

        Say("Готово. Запуск: " + Path.Combine(folder, AppExe));
        Done("Material Browser установлен.\n\nПапка: " + folder +
             "\n\nУдалить программу можно через «Программы и компоненты» или запуском Uninstall.exe.");
        return 0;
    }

    /// <summary>
    /// Where the program goes. A path that already holds something gets its own
    /// subfolder, so the installer never scatters files into a directory.
    /// </summary>
    static string TargetFolder(string requested)
    {
        string baseFolder = requested;
        if (string.IsNullOrEmpty(baseFolder))
        {
            baseFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
        }

        string first = Path.Combine(Path.GetFullPath(baseFolder), ProgramName);
        if (!Directory.Exists(first)) return first;

        // Already there: reuse it, that is a normal reinstall.
        if (File.Exists(Path.Combine(first, AppExe))) return first;

        // Something else is in the way, so step aside into our own folder.
        for (int i = 2; i < 100; i++)
        {
            string candidate = Path.Combine(Path.GetFullPath(baseFolder), ProgramName + " " + i);
            if (!Directory.Exists(candidate)) return candidate;
            if (File.Exists(Path.Combine(candidate, AppExe))) return candidate;
        }
        return first;
    }

    static void Shortcuts(string folder, bool silent)
    {
        string app = Path.Combine(folder, AppExe);
        string start = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        string programs = Path.Combine(start, "Programs");
        try
        {
            Directory.CreateDirectory(programs);
            Link(Path.Combine(programs, ProgramName + ".lnk"), app);
            Say("  ярлык  меню «Пуск»");
        }
        catch (Exception ex) { Say("  ярлык в меню «Пуск» пропущен: " + ex.Message); }

        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string existing = Path.Combine(desktop, ProgramName + ".lnk");
        if (silent && !File.Exists(existing)) return;

        try
        {
            Link(existing, app);
            Say("  ярлык  рабочий стол");
        }
        catch (Exception ex) { Say("  ярлык на рабочем столе пропущен: " + ex.Message); }
    }

    static void Link(string link, string target)
    {
        if (File.Exists(link)) File.Delete(link);
        var shell = Type.GetTypeFromProgID("WScript.Shell");
        if (shell == null) return;
        object script = Activator.CreateInstance(shell);
        try
        {
            object shortcut = shell.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod,
                null, script, new object[] { link });
            shortcut.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty,
                null, shortcut, new object[] { target });
            shortcut.GetType().InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty,
                null, shortcut, new object[] { Path.Combine(Path.GetDirectoryName(target), AppIcon) + ",0" });
            shortcut.GetType().InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod,
                null, shortcut, null);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.ReleaseComObject(script);
        }
    }

    static void Register(string folder)
    {
        try
        {
            using (Microsoft.Win32.RegistryKey key =
                Microsoft.Win32.Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                key.SetValue("DisplayName", "Material Browser");
                key.SetValue("DisplayIcon", Path.Combine(folder, AppExe));
                key.SetValue("Publisher", "Material Browser");
                key.SetValue("InstallLocation", folder);
                key.SetValue("DisplayVersion", "1.0");
                key.SetValue("NoModify", 1);
                key.SetValue("NoRepair", 1);
                key.SetValue("UninstallString", "\"" + Path.Combine(folder, "Uninstall.exe") + "\" --uninstall");
            }
            Say("  запись  «Программы и компоненты»");
        }
        catch (Exception ex) { Say("  запись в «Программы и компоненты» пропущена: " + ex.Message); }
    }

    // ─── uninstall ───────────────────────────────────────────────
    static int Uninstall()
    {
        string folder = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');

        // Only ever delete a folder that really is this program.
        if (!File.Exists(Path.Combine(folder, AppExe)))
        {
            Fail("В папке " + folder + " нет " + AppExe + ". Ничего не удаляю.");
            return 3;
        }

        Log("uninstall requested for " + folder);
        Unregister(folder);

        // A running program cannot delete itself, so hand the job to a copy.
        // The copy waits for us to exit, then removes the folder.
        string helper = Path.Combine(Path.GetTempPath(),
            "MaterialBrowser-remove-" + Process.GetCurrentProcess().Id + ".exe");
        try
        {
            CopyFile(Process.GetCurrentProcess().MainModule.FileName, helper, true);
            int mine = Process.GetCurrentProcess().Id;
            Process.Start(new ProcessStartInfo(helper,
                "--remove \"" + folder + "\" " + mine) { UseShellExecute = false });
            Log("helper started, pid " + mine + " will exit now");
            Say("Папка будет удалена сразу после выхода.");
            return 0;
        }
        catch (Exception ex)
        {
            Log("helper failed to start: " + ex.Message);
            Fail("Не удалось запустить помощник: " + ex.Message +
                 "\n\nЗакройте программу и удалите папку " + folder + " вручную.");
            return 1;
        }
    }

    /// <summary>Runs from a temporary copy, after the program itself has exited.</summary>
    static int RemoveFolder(string arg, int parentPid)
    {
        string folder = arg.Trim().Trim('"').Trim();
        if (folder.Length == 0) return 1;
        folder = Path.GetFullPath(folder.TrimEnd('\\', '/'));
        Log("helper target " + folder + " exists=" + Directory.Exists(folder) + " waitFor=" + parentPid);

        // Hard stop if the folder is not actually ours: root, a user profile,
        // the program files directory or anything outside our own name.
        if (!IsSafeToDelete(folder))
        {
            Log("refused: parent=[" + Path.GetDirectoryName(folder) + "] name=[" + Path.GetFileName(folder) + "]");
            Fail("Отказываюсь удалять " + folder + ":\nэто не папка программы.");
            return 4;
        }

        // The uninstaller itself runs out of that folder, so wait for it to go.
        for (int attempt = 0; attempt < 120 && parentPid > 0; attempt++)
        {
            bool alive;
            try { Process p = Process.GetProcessById(parentPid); alive = !p.HasExited; }
            catch { alive = false; }
            if (!alive) break;
            Thread.Sleep(250);
        }

        for (int attempt = 0; attempt < 60; attempt++)
        {
            if (CanDelete(folder)) break;
            Thread.Sleep(500);
        }

        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Log("deleted " + folder);
            Done("Material Browser удалён.\n\nПапка " + folder + " удалена.");
        }
        catch (Exception ex)
        {
            Log("delete failed: " + ex.Message);
            Fail("Не удалось удалить папку: " + ex.Message +
                 "\n\nЗакройте программу и удалите папку " + folder + " вручную.");
            return 5;
        }
        return 0;
    }

    /// <summary>
    /// A folder is ours when it sits far enough from the disk root, is not a
    /// user profile, and either is named after the program or holds the program.
    /// </summary>
    static bool IsSafeToDelete(string folder)
    {
        if (folder.Length < 6) return false;
        if (!Directory.Exists(folder)) return false;

        string parent = Path.GetDirectoryName(folder);
        if (string.IsNullOrEmpty(parent) || parent.Length < 3) return false;
        if (parent.EndsWith("\\", StringComparison.Ordinal) ||
            parent.EndsWith(":/", StringComparison.Ordinal)) return false;

        foreach (string special in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.Personal),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        })
        {
            if (string.IsNullOrEmpty(special)) continue;
            string other = Path.GetFullPath(special).TrimEnd('\\', '/');
            if (string.Equals(folder, other, StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(parent, other, StringComparison.OrdinalIgnoreCase)) return false;
        }

        // Names that mark this as our own directory.
        string name = Path.GetFileName(folder);
        if (name.StartsWith(ProgramName, StringComparison.OrdinalIgnoreCase)) return true;
        return File.Exists(Path.Combine(folder, "Uninstall.exe")) ||
               File.Exists(Path.Combine(folder, AppExe));
    }

    static bool CanDelete(string folder)
    {
        try
        {
            string probe = Path.Combine(folder, ".mb-delete-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    static void Unregister(string folder)
    {
        try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); }
        catch { }

        foreach (string root in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
        })
        {
            try
            {
                string link = Path.Combine(root, ProgramName + ".lnk");
                if (File.Exists(link)) File.Delete(link);
            }
            catch { }
        }
    }

    // ─── copy helpers ────────────────────────────────────────────
    static int CopyTree(string from, string to)
    {
        int count = 0;
        Directory.CreateDirectory(to);

        foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            string relative = file.Substring(from.Length).TrimStart('\\', '/');
            string target = Path.Combine(to, relative);
            try
            {
                CopyFile(file, target, true);
                count++;
            }
            catch { }
        }
        return count;
    }

    static void CopyFile(string from, string to, bool overwrite)
    {
        string dir = Path.GetDirectoryName(to);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.Copy(from, to, overwrite);
    }

    static void Say(string message) { Console.WriteLine(message); }

    /// <summary>Appends to a log so a detached helper can still be followed.</summary>
    static void Log(string message)
    {
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "MaterialBrowser-install.log"),
                DateTime.Now.ToString("HH:mm:ss") + " pid " + Process.GetCurrentProcess().Id + "  " + message + "\r\n");
        }
        catch { }
    }

    /// <summary>Reports a problem both to the log and to the person sitting there.</summary>
    static void Fail(string message)
    {
        try { Console.Error.WriteLine(message); } catch { }
        try
        {
            if (Environment.UserInteractive)
                MessageBox.Show(message, ProgramName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch { }
    }

    /// <summary>The same, for the happy ending.</summary>
    static void Done(string message)
    {
        Say(message);
        try
        {
            if (Environment.UserInteractive)
                MessageBox.Show(message, ProgramName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch { }
    }
}