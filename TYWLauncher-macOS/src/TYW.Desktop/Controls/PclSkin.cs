using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;

namespace TYW.Desktop.Controls;

/// <summary>
/// 皮肤头像。对应 Windows 版 <c>Pages/PageLaunch/MySkin.xaml</c>：
/// 64×64 半透明底，正脸层 48×48（贴图 8,8,8,8），头发附加层 56×56（贴图 40,8,8,8），
/// 两层都按最近邻放大，所以是「Steve 的正脸」而不是全身像。
/// </summary>
public class PclSkin : Grid
{
    private static Bitmap? _defaultSkin;
    private static readonly object Gate = new();

    private readonly Image _face;
    private readonly Image _hat;

    public PclSkin()
    {
        Width = 64;
        Height = 64;
        Background = Pcl.Res("ColorBrushSemiTransparent");
        RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        RenderTransform = new ScaleTransform(1, 1);

        _face = MakeImage(48);
        _hat = MakeImage(56);
        Children.Add(_face);
        Children.Add(_hat);

        PointerEntered += (_, _) => Scale(1.06, 160);
        PointerExited += (_, _) => Scale(1.0, 160);

        var skin = DefaultSkin();
        if (skin != null) Apply(skin);
    }

    /// <summary>换成指定皮肤贴图（64×64 或 64×32）。</summary>
    public void Apply(Bitmap skin)
    {
        try
        {
            _face.Source = new CroppedBitmap(skin, new PixelRect(8, 8, 8, 8));
            _hat.Source = skin.PixelSize.Height >= 64
                ? new CroppedBitmap(skin, new PixelRect(40, 8, 8, 8))
                : null;
        }
        catch
        {
            // 贴图尺寸不对就保持默认头像
        }
    }

    /// <summary>从本地皮肤文件加载（ModSkin 缓存目录）。</summary>
    public void ApplyFile(string path)
    {
        try
        {
            using var fs = System.IO.File.OpenRead(path);
            Apply(new Bitmap(fs));
        }
        catch (Exception ex)
        {
            ModLog.Err(ex, "加载皮肤头像 " + path);
        }
    }

    private static Image MakeImage(double size)
    {
        var img = new Image
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Fill,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        // 最近邻放大，保持像素风（对应 Windows 版 BitmapScalingMode="NearestNeighbor"）
        RenderOptions.SetBitmapInterpolationMode(img, BitmapInterpolationMode.None);
        return img;
    }

    private static Bitmap? DefaultSkin()
    {
        lock (Gate)
        {
            if (_defaultSkin != null) return _defaultSkin;
            try
            {
                using var stream = AssetLoader.Open(new Uri("avares://TYWLauncher/Assets/Skins/Steve.png"));
                _defaultSkin = new Bitmap(stream);
            }
            catch (Exception ex)
            {
                ModLog.Err(ex, "载入默认皮肤");
            }
            return _defaultSkin;
        }
    }

    private void Scale(double to, int ms)
    {
        if (RenderTransform is not ScaleTransform st) return;
        double from = st.ScaleX;
        _ = Ani.Tween(from, to, ms, v =>
        {
            st.ScaleX = v;
            st.ScaleY = v;
        }, Ani.EaseOutFluent);
    }
}
