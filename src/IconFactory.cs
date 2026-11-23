using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace RelogioAcompanhamento
{
    internal static class IconFactory
    {
        public static Icon CreateClockIcon(Color accentColor, bool isPaused)
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);

                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(24, 26, 32)))
                    {
                        g.FillEllipse(bg, 2, 2, 28, 28);
                    }

                    using (Pen ring = new Pen(accentColor, 3.0f))
                    {
                        g.DrawEllipse(ring, 3, 3, 26, 26);
                    }

                    if (isPaused)
                    {
                        using (SolidBrush bar = new SolidBrush(Color.FromArgb(226, 232, 240)))
                        {
                            g.FillRectangle(bar, 11, 10, 3.5f, 12);
                            g.FillRectangle(bar, 17.5f, 10, 3.5f, 12);
                        }
                    }
                    else
                    {
                        using (Pen hand = new Pen(Color.FromArgb(248, 250, 252), 2.4f))
                        {
                            hand.StartCap = LineCap.Round;
                            hand.EndCap = LineCap.Round;
                            g.DrawLine(hand, 16, 16, 16, 9);
                            g.DrawLine(hand, 16, 16, 22, 16);
                        }
                        using (SolidBrush dot = new SolidBrush(accentColor))
                        {
                            g.FillEllipse(dot, 14, 14, 4, 4);
                        }
                    }
                }

                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    using (Icon temp = Icon.FromHandle(hIcon))
                    {
                        return (Icon)temp.Clone();
                    }
                }
                finally
                {
                    NativeMethods.DestroyIcon(hIcon);
                }
            }
        }
    }
}
