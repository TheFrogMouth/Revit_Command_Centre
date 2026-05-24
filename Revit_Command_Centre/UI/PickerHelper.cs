using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Revit_Command_Centre.UI
{
    /// <summary>
    /// Border-based segmented picker — replaces WPF ComboBox entirely.
    /// ComboBox and Button default ControlTemplates use D3D9 gradient/blur effects
    /// that crash on AMD RX 9060 XT inside Revit's rendering context.
    /// This helper uses only Border + TextBlock + MouseLeftButtonUp — no ControlTemplate.
    /// </summary>
    public class Picker
    {
        public string[] Options { get; }
        public int Index { get; set; }
        public string Value => Options[Index];

        public Picker(string[] options, int defaultIndex = 0)
        {
            Options = options;
            Index   = defaultIndex;
        }
    }

    public static class PickerHelper
    {
        private static readonly SolidColorBrush ActiveBorder   = Freeze(0xFF, 0x18, 0x5F, 0xA5);
        private static readonly SolidColorBrush ActiveBg       = Freeze(0xFF, 0xE6, 0xF1, 0xFB);
        private static readonly SolidColorBrush ActiveText     = Freeze(0xFF, 0x18, 0x5F, 0xA5);
        private static readonly SolidColorBrush InactiveBorder = Freeze(0x1E, 0x00, 0x00, 0x00);
        private static readonly SolidColorBrush InactiveBg     = new(Colors.White);
        private static readonly SolidColorBrush InactiveText   = Freeze(0xFF, 0x6B, 0x6B, 0x6B);
        private static readonly FontFamily      SegoeUI        = new("Segoe UI");

        static PickerHelper()
        {
            InactiveBg.Freeze();
        }

        private static SolidColorBrush Freeze(byte a, byte r, byte g, byte b)
        {
            var b2 = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            b2.Freeze();
            return b2;
        }

        /// <summary>
        /// Renders the picker options into <paramref name="panel"/> as a vertical list of
        /// Border items. Call again after changing <see cref="Picker.Index"/> to refresh.
        /// </summary>
        public static void Refresh(StackPanel panel, Picker picker, Action? onChange = null)
        {
            panel.Children.Clear();
            panel.Orientation = Orientation.Vertical;

            for (int i = 0; i < picker.Options.Length; i++)
            {
                int   idx = i;
                bool  sel = i == picker.Index;

                var row = new Border
                {
                    Height          = 28,
                    Margin          = new Thickness(0, 0, 0, 2),
                    Padding         = new Thickness(8, 0, 8, 0),
                    CornerRadius    = new CornerRadius(4),
                    BorderThickness = new Thickness(1),
                    BorderBrush     = sel ? ActiveBorder : InactiveBorder,
                    Background      = sel ? ActiveBg     : InactiveBg,
                    Cursor          = Cursors.Hand,
                    Child = new TextBlock
                    {
                        Text              = picker.Options[i],
                        FontSize          = 11,
                        FontFamily        = SegoeUI,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground        = sel ? ActiveText : InactiveText
                    }
                };

                row.MouseLeftButtonUp += (_, _) =>
                {
                    picker.Index = idx;
                    Refresh(panel, picker, onChange);
                    onChange?.Invoke();
                };

                panel.Children.Add(row);
            }
        }

        /// <summary>
        /// Creates a Browse/action button as a Border (no WPF Button ControlTemplate).
        /// </summary>
        public static Border MakeButton(string label, MouseButtonEventHandler onClick,
            double height = 32, Thickness? margin = null)
        {
            var btn = new Border
            {
                Height          = height,
                Margin          = margin ?? new Thickness(0),
                Padding         = new Thickness(14, 0, 14, 0),
                CornerRadius    = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                BorderBrush     = InactiveBorder,
                Background      = InactiveBg,
                Cursor          = Cursors.Hand,
                Child = new TextBlock
                {
                    Text              = label,
                    FontSize          = 12,
                    FontFamily        = SegoeUI,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground        = InactiveText
                }
            };
            btn.MouseLeftButtonUp += onClick;
            return btn;
        }
    }
}
