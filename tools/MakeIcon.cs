using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace IconTool
{
    /// <summary>Generates the application icon (multi-size ICO): a Material You tile with a globe mark.</summary>
    static class MakeIcon
    {
        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);

        static Bitmap DrawMark(int size)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(Color.Transparent);

                float s = size;
                Rectangle rect = new Rectangle(0, 0, size, size);

                using (LinearGradientBrush brush = new LinearGradientBrush(rect,
                    Color.FromArgb(255, 103, 80, 164), Color.FromArgb(255, 144, 202, 249),
                    LinearGradientMode.ForwardDiagonal))
                using (GraphicsPath path = RoundRect(rect, s * 0.28f))
                    g.FillPath(brush, path);

                // A pair of arcs: the browser's "orbit" around the globe.
                float cx = s * 0.5f, cy = s * 0.5f;
                float r = s * 0.26f;
                float stroke = Math.Max(1f, s * 0.075f);
                using (Pen pen = new Pen(Color.FromArgb(240, 255, 255, 255), stroke))
                {
                    g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
                    RectangleF flat = new RectangleF(cx - r * 0.46f, cy - r, r * 0.92f, r * 2);
                    g.DrawEllipse(pen, flat);
                }
                using (Pen pen = new Pen(Color.FromArgb(200, 255, 255, 255), Math.Max(1f, s * 0.055f)))
                    g.DrawLine(pen, cx - r, cy, cx + r, cy);
            }
            return bmp;
        }

        static GraphicsPath RoundRect(Rectangle r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - 1 - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - 1 - d, r.Bottom - 1 - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - 1 - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        static byte[] IconImage(Bitmap bmp, out int width, out int height)
        {
            IntPtr handle = bmp.GetHicon();
            try
            {
                using (Icon icon = Icon.FromHandle(handle))
                using (MemoryStream ms = new MemoryStream())
                {
                    icon.Save(ms);
                    byte[] file = ms.ToArray();
                    width = file[6];
                    height = file[7];
                    int length = BitConverter.ToInt32(file, 14);
                    byte[] image = new byte[length];
                    Array.Copy(file, 22, image, 0, length);
                    return image;
                }
            }
            finally { DestroyIcon(handle); }
        }

        static int Main(string[] args)
        {
            string output = args.Length > 0 ? args[0] : "MaterialBrowser.ico";
            int[] sizes = { 16, 24, 32, 48, 64, 128 };
            var images = new List<byte[]>();
            var dims = new List<int>();

            foreach (int size in sizes)
            {
                using (Bitmap bmp = DrawMark(size))
                {
                    int w, h;
                    byte[] image = IconImage(bmp, out w, out h);
                    images.Add(image);
                    dims.Add(w);
                }
            }

            if (args.Length > 1)
            {
                using (Bitmap big = DrawMark(256))
                    big.Save(args[1], ImageFormat.Png);
                Console.WriteLine("preview written: " + args[1]);
            }

            using (FileStream fs = new FileStream(output, FileMode.Create, FileAccess.Write))
            using (BinaryWriter w = new BinaryWriter(fs))
            {
                int count = images.Count;
                int offset = 6 + 16 * count;
                w.Write((ushort)0);
                w.Write((ushort)1);
                w.Write((ushort)count);
                for (int i = 0; i < count; i++)
                {
                    w.Write((byte)dims[i]);
                    w.Write((byte)dims[i]);
                    w.Write((byte)0);
                    w.Write((byte)0);
                    w.Write((ushort)1);
                    w.Write((ushort)32);
                    w.Write((uint)images[i].Length);
                    w.Write((uint)offset);
                    offset += images[i].Length;
                }
                foreach (byte[] image in images) w.Write(image);
            }

            Console.WriteLine("icon written: " + output + " (" + new FileInfo(output).Length + " bytes)");
            return 0;
        }
    }
}