// Builds MaterialBrowser.ico: a Material You style rounded square with an
// "M" cut into it, rendered at every size Windows asks for.
//
//   csc /nologo /out:IcoGen.exe /r:System.Drawing.dll IcoGen.cs
//   IcoGen.exe MaterialBrowser.ico

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

static class IcoGen
{
    static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

    // Material 3 style primary ramp: a violet leaning indigo, light on top.
    static readonly Color Top = Color.FromArgb(255, 124, 108, 255);
    static readonly Color Bottom = Color.FromArgb(255, 79, 70, 229);

    static void Main(string[] args)
    {
        string outPath = args.Length > 0 ? args[0] : "MaterialBrowser.ico";

        var images = new List<byte[]>();
        foreach (int size in Sizes) images.Add(Bmp(size));

        var dir = new byte[6];
        dir[2] = 1;
        dir[4] = (byte)images.Count;

        int offset = 6 + 16 * images.Count;
        var entries = new byte[16 * images.Count];
        for (int i = 0; i < images.Count; i++)
        {
            int size = Sizes[i];
            int at = i * 16;
            entries[at + 0] = (byte)(size >= 256 ? 0 : size);   // 0 means 256
            entries[at + 1] = (byte)(size >= 256 ? 0 : size);
            entries[at + 2] = 0;                               // palette size
            entries[at + 3] = 0;                               // reserved
            entries[at + 4] = 1;                               // planes
            entries[at + 5] = 0;
            entries[at + 6] = 32;                              // bits per pixel
            entries[at + 7] = 0;
            Write(entries, at + 8, images[i].Length);
            Write(entries, at + 12, offset);
            offset += images[i].Length;
        }

        using (var stream = new FileStream(outPath, FileMode.Create, FileAccess.Write))
        {
            stream.Write(dir, 0, dir.Length);
            stream.Write(entries, 0, entries.Length);
            foreach (byte[] image in images) stream.Write(image, 0, image.Length);
        }

        Console.WriteLine("wrote " + outPath + " (" + images.Count + " sizes, " +
                          new FileInfo(outPath).Length + " bytes)");
        if (args.Length > 1) Directory.CreateDirectory(args[1]);
        if (args.Length > 1)
        {
            using (var png = Render(256)) png.Save(Path.Combine(args[1], "icon256.png"), ImageFormat.Png);
            using (var png = Render(64)) png.Save(Path.Combine(args[1], "icon64.png"), ImageFormat.Png);
            using (var png = Render(32)) png.Save(Path.Combine(args[1], "icon32.png"), ImageFormat.Png);
            using (var png = Render(16)) png.Save(Path.Combine(args[1], "icon16.png"), ImageFormat.Png);
        }
    }

    /// <summary>One icon image as a 32bpp BMP with the AND mask Windows expects.</summary>
    static byte[] Bmp(int size)
    {
        using (Bitmap bmp = Render(size))
        {
            var data = bmp.LockBits(new Rectangle(0, 0, size, size),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                var pixels = new byte[stride * size];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

                // BMP rows run bottom-up, and the alpha sits in the high byte:
                // flip the vertical axis so the icon is not upside down.
                var rows = new byte[pixels.Length];
                for (int y = 0; y < size; y++)
                    Buffer.BlockCopy(pixels, y * stride, rows, (size - 1 - y) * stride, stride);

                int maskStride = ((size + 31) / 32) * 4;
                var mask = new byte[maskStride * size];   // alpha drives transparency
                int header = 40;
                int total = header + rows.Length + mask.Length;

                var file = new byte[total];
                Write(file, 0, 40);
                Write(file, 4, size);
                Write(file, 8, size * 2);                  // height counts mask too
                Write(file, 12, 1);
                Write(file, 14, 32);
                Write(file, 20, rows.Length + mask.Length);
                Buffer.BlockCopy(rows, 0, file, header, rows.Length);
                return file;
            }
            finally { bmp.UnlockBits(data); }
        }
    }

    static Bitmap Render(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(Color.Transparent);

            float s = size;
            // Small sizes need a fatter mark to survive the downscale.
            float inset = size <= 24 ? 0.5f : 1f;
            var box = new RectangleF(inset, inset, s - inset * 2, s - inset * 2);
            // RoundedRect works in pixels, so scale the fraction to the icon size.
            float radius = (size <= 24 ? 0.19f : 0.26f) * s;

            using (GraphicsPath path = RoundedRect(box, radius))
            using (var brush = new LinearGradientBrush(
                new PointF(box.X, box.Y), new PointF(box.X, box.Bottom),
                Top, Bottom))
            {
                g.FillPath(brush, path);
            }

            // The letter: a geometric M, thick enough to read at 16px.
            float stem = size <= 24 ? 0.26f : 0.19f;
            float left = box.X + box.Width * (size <= 24 ? 0.15f : 0.20f);
            float right = box.Right - box.Width * (size <= 24 ? 0.15f : 0.20f);
            float top = box.Y + box.Height * (size <= 24 ? 0.22f : 0.26f);
            float bottom = box.Bottom - box.Height * (size <= 24 ? 0.20f : 0.26f);
            float w = right - left, h = bottom - top;

            using (GraphicsPath letter = new GraphicsPath())
            {
                // Traced clockwise: outer top edge, down the right stem, back along
                // the inside. Both bottom corners need their own vertex, or the
                // closing edge cuts the stems into wedges instead of bars.
                letter.AddPolygon(new[]
                {
                    P(left, top), P(left + w * 0.5f, top + h * 0.52f), P(right, top),
                    P(right, bottom), P(right - w * stem, bottom), P(right - w * stem, top + h * 0.46f),
                    P(left + w * 0.5f, top + h * 0.80f), P(left + w * stem, top + h * 0.46f),
                    P(left + w * stem, bottom), P(left, bottom),
                });
                g.FillPath(Brushes.White, letter);
            }
        }
        return bmp;
    }

    static PointF P(float x, float y) { return new PointF(x, y); }

    static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        if (d <= 0) { var p = new GraphicsPath(); p.AddRectangle(r); return p; }
        if (d > r.Width) d = r.Width;
        if (d > r.Height) d = r.Height;

        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    static void Write(byte[] buffer, int at, int value)
    {
        buffer[at + 0] = (byte)value;
        buffer[at + 1] = (byte)(value >> 8);
        buffer[at + 2] = (byte)(value >> 16);
        buffer[at + 3] = (byte)(value >> 24);
    }
}