using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace TYW.Desktop.Controls;

/// <summary>
/// 内置图标（对应 Windows 版 Images/Blocks/*.png）。
/// 版本图标不是手画的矢量图，而是 Minecraft 原版方块贴图：
/// 正式版 Grass、快照 CommandBlock、远古 CobbleStone、愚人节 GoldBlock、
/// Forge Anvil、NeoForge NeoForge、Fabric Fabric、OptiFine GrassPath、LiteLoader Egg、基岩 Bedrock。
/// </summary>
internal static class PclIcons
{
    private static readonly Dictionary<string, Bitmap?> Cache = new();

    /// <summary>取方块贴图（不带扩展名）。找不到返回 null。</summary>
    public static Bitmap? Block(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (Cache.TryGetValue(name, out var cached)) return cached;
        Bitmap? bmp = null;
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://TYWLauncher/Assets/Blocks/" + name + ".png"));
            bmp = new Bitmap(stream);
        }
        catch
        {
            // 资源缺失就退回没有图标
        }
        Cache[name] = bmp;
        return bmp;
    }

    /// <summary>方块贴图 Image 控件（像素风，保持锐利）。</summary>
    public static Control? BlockImage(string name, double size)
    {
        var bmp = Block(name);
        if (bmp is null) return null;
        var img = new Image
        {
            Source = bmp,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center
        };
        RenderOptions.SetBitmapInterpolationMode(img, BitmapInterpolationMode.HighQuality);
        return img;
    }
}
