using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace TYW.Desktop.Controls;

/// <summary>
/// 输入框 / 下拉框。对应 Windows 版 <c>Controls/MyComboBox</c>：
/// 白底、Gray5 描边、圆角 3、高度 28、左侧文字内边距 10、右侧下拉箭头。
/// 可编辑模式直接当输入框用（离线用户名），不可编辑模式只能从列表里选（版本、下载源）。
/// </summary>
public class PclComboBox : Grid
{
    private readonly Border _box;
    private readonly TextBox _edit;
    private readonly TextBlock _label;
    private readonly Path _arrow;
    private readonly Popup _popup;
    private readonly StackPanel _items;
    private readonly ScrollViewer _scroll;

    private readonly List<string> _source = new();
    private int _selectedIndex = -1;
    private bool _editable = true;
    private bool _open;

    public PclComboBox()
    {
        Height = 28;
        MinHeight = 28;
        VerticalAlignment = VerticalAlignment.Center;

        _label = new TextBlock
        {
            FontSize = 12.5,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrushGray4"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 24, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        _edit = new TextBox
        {
            FontSize = 12.5,
            FontFamily = Pcl.FontFamily,
            Foreground = Pcl.Res("ColorBrushGray1"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            MinHeight = 0,
            Height = 26,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 24, 0),
            Watermark = "",
            WatermarkForeground = Pcl.Res("ColorBrushGray4"),
            CaretBrush = Pcl.Res("ColorBrushGray1")
        };
        _edit.TextChanged += (_, _) => TextChanged?.Invoke(this, EventArgs.Empty);

        _arrow = new Path
        {
            Data = Geometry.Parse("M0,0 L5,5 L10,0"),
            Stroke = Pcl.Res("ColorBrushGray3"),
            StrokeThickness = 1.4,
            StrokeLineCap = PenLineCap.Round,
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 1, 12, 0),
            IsHitTestVisible = true
        };
        _arrow.PointerPressed += (_, e) =>
        {
            if (_source.Count == 0) return;
            e.Handled = true;
            Toggle();
        };

        var host = new Grid();
        host.Children.Add(_label);
        host.Children.Add(_edit);
        host.Children.Add(_arrow);

        _box = new Border
        {
            Height = 28,
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(1),
            BorderBrush = Pcl.Res("ColorBrushGray5"),
            Background = Pcl.Res("ColorBrushWhite"),
            Child = host
        };
        _box.PointerEntered += (_, _) => UpdateBorder(true);
        _box.PointerExited += (_, _) => UpdateBorder(false);
        _box.PointerPressed += (_, e) =>
        {
            if (_editable || _source.Count == 0) return;
            e.Handled = true;
            Toggle();
        };
        Children.Add(_box);

        _items = new StackPanel();
        _scroll = new ScrollViewer
        {
            MaxHeight = 220,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _items
        };
        var pop = new Border
        {
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(1),
            BorderBrush = Pcl.Res("ColorBrushGray5"),
            Background = Pcl.Res("ColorBrushWhite"),
            Padding = new Thickness(3, 4, 3, 4),
            BoxShadow = BoxShadows.Parse("0 4 12 0 #26000000"),
            Child = _scroll
        };
        _popup = new Popup
        {
            PlacementTarget = _box,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            IsLightDismissEnabled = true,
            VerticalOffset = 4,
            Child = pop
        };
        _popup.Closed += (_, _) => { _open = false; UpdateBorder(false); };
        Children.Add(_popup);

        SetEditable(true);
    }

    /// <summary>文字内容（可编辑模式）。</summary>
    public string Text
    {
        get => _editable ? (_edit.Text ?? "") : (_selectedIndex >= 0 && _selectedIndex < _source.Count ? _source[_selectedIndex] : "");
        set
        {
            if (_editable)
            {
                _edit.Text = value ?? "";
            }
            else
            {
                int idx = _source.IndexOf(value ?? "");
                SelectedIndex = idx;
            }
        }
    }

    /// <summary>占位文字（对应 MyComboBox.HintText / 水印）。</summary>
    public string Placeholder
    {
        get => _placeholder;
        set
        {
            _placeholder = value ?? "";
            if (_editable)
            {
                _edit.Watermark = _placeholder;
            }
            else if (_selectedIndex < 0)
            {
                _label.Text = _placeholder;
                _label.Foreground = Pcl.Res("ColorBrushGray4");
            }
        }
    }

    public bool IsEditable
    {
        get => _editable;
        set => SetEditable(value);
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            _selectedIndex = value;
            if (!_editable)
            {
                bool has = value >= 0 && value < _source.Count;
                _label.Text = has ? _source[value] : _placeholder;
                _label.Foreground = has ? Pcl.Res("ColorBrushGray1") : Pcl.Res("ColorBrushGray4");
            }
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string SelectedItem => _selectedIndex >= 0 && _selectedIndex < _source.Count ? _source[_selectedIndex] : null;

    private string _placeholder = "";

    public IReadOnlyList<string> Items => _source;

    public void SetItems(IEnumerable<string> items, int select = -1)
    {
        _source.Clear();
        _source.AddRange(items ?? Array.Empty<string>());
        RebuildPopup();
        if (!_editable)
        {
            int idx = select >= 0 ? select : (_source.Count > 0 ? 0 : -1);
            SelectedIndex = idx;
        }
    }

    public event EventHandler TextChanged;
    public event EventHandler SelectionChanged;

    private void SetEditable(bool editable)
    {
        _editable = editable;
        _edit.IsVisible = editable;
        _label.IsVisible = !editable;
        _arrow.IsVisible = editable || _source.Count > 0;
        Cursor = new Cursor(editable ? StandardCursorType.Ibeam : StandardCursorType.Hand);
        _edit.Cursor = new Cursor(StandardCursorType.Ibeam);
    }

    private void Toggle()
    {
        if (_source.Count == 0) return;
        _popup.IsOpen = !_popup.IsOpen;
        _open = _popup.IsOpen;
        UpdateBorder(_open);
    }

    private void UpdateBorder(bool hot)
    {
        _box.BorderBrush = hot ? Pcl.Res("ColorBrush3") : Pcl.Res("ColorBrushGray5");
    }

    private void RebuildPopup()
    {
        _items.Children.Clear();
        for (int i = 0; i < _source.Count; i++)
        {
            int idx = i;
            var lab = new TextBlock
            {
                Text = _source[i],
                FontSize = 12.5,
                FontFamily = Pcl.FontFamily,
                Foreground = idx == _selectedIndex ? Pcl.Res("ColorBrush3") : Pcl.Res("ColorBrushGray1"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var row = new Border
            {
                Height = 28,
                CornerRadius = new CornerRadius(3),
                Background = Brushes.Transparent,
                Padding = new Thickness(7, 0, 7, 0),
                Child = lab
            };
            row.PointerEntered += (_, _) => row.Background = Pcl.Res("ColorBrush7");
            row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
            row.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                _popup.IsOpen = false;
                if (_editable) Text = _source[idx];
                else SelectedIndex = idx;
            };
            _items.Children.Add(row);
        }
    }
}
