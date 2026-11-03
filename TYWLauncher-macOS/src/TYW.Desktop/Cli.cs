using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using TYW;

namespace TYW.Desktop;

/// <summary>
/// 命令行入口，用于在没有图形界面或排查问题时直接验证下载/启动链路。
/// 不带参数时照常启动图形界面。
/// </summary>
internal static class Cli
{
    /// <summary>返回 -1 表示继续走图形界面，其它值作为进程退出码。</summary>
    public static int TryRun(string[] args)
    {
        if (args.Length == 0) return -1;
        ModPaths.EnsureAll();
        ModSettings.Load();
        StaticMirror();

        try
        {
            switch (args[0])
            {
                case "--java":
                    return ListJava();
                case "--versions":
                    return ListVersions();
                case "--install":
                    return Install(args.Length > 1 ? args[1] : null);
                case "--launch":
                    return Launch(args);
                case "--help":
                case "-h":
                    Console.WriteLine("用法：");
                    Console.WriteLine("  TYWLauncher                      启动图形界面");
                    Console.WriteLine("  TYWLauncher --java               列出检测到的 Java");
                    Console.WriteLine("  TYWLauncher --versions           列出已安装版本");
                    Console.WriteLine("  TYWLauncher --install <版本号>    下载安装原版");
                    Console.WriteLine("  TYWLauncher --launch <版本号> [--name 玩家名] [--ram 2048]");
                    return 0;
                default:
                    return -1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("错误：" + ex.Message);
            return 1;
        }
    }

    private static void StaticMirror()
    {
        ModDownload.Mirror = ModSettings.GetBool("UseOfficialSource")
            ? ModDownload.OfficialBase
            : "https://bmclapi2.bangbang93.com";
    }

    private static int ListJava()
    {
        var list = ModJava.ListJava();
        if (list.Count == 0)
        {
            Console.WriteLine("没有检测到 Java。");
            return 1;
        }
        foreach (var j in list)
            Console.WriteLine($"Java {j.Major}\t{j.Arch}\t{j.FullVersion}\t{j.Path}");
        return 0;
    }

    private static int ListVersions()
    {
        var list = ModVersion.ListVersions(true);
        if (list.Count == 0)
        {
            Console.WriteLine("没有已安装的版本（" + ModPaths.McFolder + "）");
            return 1;
        }
        foreach (var v in list) Console.WriteLine(v.Id + "\t" + v.Type);
        return 0;
    }

    private static int Install(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            Console.WriteLine("请指定版本号，例如 --install 1.21.1");
            return 1;
        }
        var all = ModDownload.Manifest();
        var target = all.FirstOrDefault(v => v.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (target == null)
        {
            Console.WriteLine("版本不存在：" + id);
            return 1;
        }
        Console.WriteLine($"开始安装 {target.Id}（源：{ModDownload.Active}）");
        var lastPct = -1;
        ModDownload.Install(target.Id, target.Url, (p, stage) =>
        {
            int pct = (int)(p * 100);
            if (pct != lastPct)
            {
                lastPct = pct;
                Console.WriteLine($"[{pct,3}%] {stage}");
            }
        }, () => false);
        Console.WriteLine("安装完成：" + ModPaths.McFolder);
        return 0;
    }

    private static int Launch(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("请指定版本号，例如 --launch 1.21.1");
            return 1;
        }
        var opt = new LaunchOptions
        {
            VersionId = args[1],
            PlayerName = ArgValue(args, "--name") ?? "Player",
            MaxRamMb = int.TryParse(ArgValue(args, "--ram"), out var ram) ? ram : 2048,
            GameDir = ModPaths.McFolder
        };
        opt.Uuid = McOffline.UuidOf(opt.PlayerName);
        Console.WriteLine($"启动 {opt.VersionId}（玩家 {opt.PlayerName}，内存 {opt.MaxRamMb}MB）");
        var p = ModLaunch.Launch(opt, (stage, prog) => Console.WriteLine($"[{(int)(prog * 100),3}%] {stage}"));
        Console.WriteLine("进程已创建，等待游戏退出…");
        p.WaitForExit();
        Console.WriteLine("游戏退出码：" + p.ExitCode);
        return p.ExitCode;
    }

    private static string ArgValue(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }
}
