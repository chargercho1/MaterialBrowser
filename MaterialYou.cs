using System;
using System.Drawing;

namespace MaterialBrowser
{
    // ─── Color space helpers: sRGB → CIELAB / LCh, with gamut mapping ───
    public static class ColorEngine
    {
        public static double Linearize(double c)
        {
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        public static double Delinearize(double c)
        {
            return c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1.0 / 2.4) - 0.055;
        }

        public static void ToLab(Color col, out double L, out double a, out double b)
        {
            double r = Linearize(col.R / 255.0);
            double g = Linearize(col.G / 255.0);
            double bl = Linearize(col.B / 255.0);

            double x = (r * 0.4124564 + g * 0.3575761 + bl * 0.1804375) / 0.95047;
            double y = (r * 0.2126729 + g * 0.7151522 + bl * 0.0721750);
            double z = (r * 0.0193339 + g * 0.1191920 + bl * 0.9503041) / 1.08883;

            double fx = F(x), fy = F(y), fz = F(z);
            L = 116.0 * fy - 16.0;
            a = 500.0 * (fx - fy);
            b = 200.0 * (fy - fz);
        }

        private static double F(double t)
        {
            return t > 0.008856451679035631 ? Cbrt(t) : (903.2962962962963 * t + 16.0) / 116.0;
        }

        private static double Cbrt(double t)
        {
            if (t < 0) return -Math.Pow(-t, 1.0 / 3.0);
            return Math.Pow(t, 1.0 / 3.0);
        }

        public static void ToLCh(Color col, out double L, out double chroma, out double hue)
        {
            double a, b;
            ToLab(col, out L, out a, out b);
            chroma = Math.Sqrt(a * a + b * b);
            hue = Math.Atan2(b, a) * 180.0 / Math.PI;
            if (hue < 0) hue += 360.0;
        }

        /// <summary>Converts LCh (L 0..100) to sRGB, reducing chroma until the color is inside the gamut.</summary>
        public static Color FromLCh(double L, double chroma, double hue)
        {
            double rad = hue * Math.PI / 180.0;
            double[] lin = LinFromLabChroma(L, chroma, Math.Cos(rad), Math.Sin(rad));
            if (lin == null) return FromLCh(L, 0, hue);
            return ToColor(lin[0], lin[1], lin[2]);
        }

        private static double[] LinFromLabChroma(double L, double chroma, double ca, double sa)
        {
            double lo = 0, hi = chroma;
            double[] best = null;
            for (int i = 0; i < 24; i++)
            {
                double mid = (lo + hi) / 2.0;
                double[] cand = LabChromaToLinear(L, mid * ca, mid * sa);
                if (cand != null && InGamut(cand)) { best = cand; lo = mid; }
                else hi = mid;
            }
            if (best == null)
            {
                double[] cand = LabChromaToLinear(L, 0, 0);
                best = cand;
            }
            return best;
        }

        private static bool InGamut(double[] lin)
        {
            return lin[0] >= -0.0001 && lin[0] <= 1.0001 &&
                   lin[1] >= -0.0001 && lin[1] <= 1.0001 &&
                   lin[2] >= -0.0001 && lin[2] <= 1.0001;
        }

        private static double[] LabChromaToLinear(double L, double a, double b)
        {
            double fy = (L + 16.0) / 116.0;
            double fx = fy + a / 500.0;
            double fz = fy - b / 200.0;
            double x = InvF(fx) * 0.95047;
            double y = InvF(fy);
            double z = InvF(fz) * 1.08883;
            return new double[]
            {
                 3.2404542 * x - 1.5371385 * y - 0.4985314 * z,
                -0.9692660 * x + 1.8760108 * y + 0.0415560 * z,
                 0.0556434 * x - 0.2040259 * y + 1.0572252 * z
            };
        }

        private static double InvF(double t)
        {
            double t3 = t * t * t;
            return t3 > 0.008856451679035631 ? t3 : (116.0 * t - 16.0) / 903.2962962962963;
        }

        public static Color ToColor(double lr, double lg, double lb)
        {
            int r = (int)Math.Round(Clamp255(Delinearize(lr)));
            int g = (int)Math.Round(Clamp255(Delinearize(lg)));
            int b = (int)Math.Round(Clamp255(Delinearize(lb)));
            return Color.FromArgb(255, r, g, b);
        }

        private static double Clamp255(double v)
        {
            return v < 0 ? 0 : (v > 1 ? 1 : v) * 255.0;
        }

        public static Color WithAlpha(this Color c, int alpha)
        {
            return Color.FromArgb(alpha, c.R, c.G, c.B);
        }
    }

    /// <summary>Material 3 tonal palette: one hue + one chroma, tones 0..100.</summary>
    public class TonalPalette
    {
        private readonly double _hue;
        private readonly double _chroma;
        private readonly Color[] _cache = new Color[101];

        public TonalPalette(double hue, double chroma)
        {
            _hue = ((hue % 360.0) + 360.0) % 360.0;
            _chroma = chroma;
        }

        public Color Tone(int tone)
        {
            if (tone < 0) tone = 0;
            if (tone > 100) tone = 100;
            if (_cache[tone].IsEmpty) _cache[tone] = ColorEngine.FromLCh(tone, _chroma, _hue);
            return _cache[tone];
        }

        public Color[] Ramp()
        {
            var list = new Color[11];
            for (int i = 0; i <= 10; i++) list[i] = Tone(i * 10);
            return list;
        }

        /// <summary>True when a tone needs white/dark text on top of it.</summary>
        public bool IsDark(int tone) { return Tone(tone).GetBrightness() < 0.55; }
    }

    /// <summary>A full Material 3 color scheme (light or dark) derived from one source color.</summary>
    public class Scheme
    {
        public bool Dark;
        public TonalPalette Primary;
        public TonalPalette Secondary;
        public TonalPalette Tertiary;
        public TonalPalette Neutral;
        public TonalPalette NeutralVariant;
        public TonalPalette Error;
        public Color Source;
        public double Hue;

        // System role colors (assigned in Build())
        public Color OnPrimary, PrimaryContainer, OnPrimaryContainer;
        public Color OnSecondary, SecondaryContainer, OnSecondaryContainer;
        public Color OnTertiary, TertiaryContainer, OnTertiaryContainer;
        public Color OnError, ErrorContainer, OnErrorContainer;
        public Color Background, OnBackground;
        public Color Surface, OnSurface, SurfaceVariant, OnSurfaceVariant;
        public Color Outline, OutlineVariant;
        public Color SurfaceLowest, SurfaceLow, SurfaceMid, SurfaceHigh, SurfaceHighest;
        public Color InverseSurface, InverseOnSurface, InversePrimary, Scrim;
        public Color Shadow;

        public static Scheme FromSource(Color source, bool dark, int contrast)
        {
            double L, c, h;
            ColorEngine.ToLCh(source, out L, out c, out h);
            if (c < 18.0) c = 18.0;          // ensure a usable hue direction
            if (c > 84.0) c = 84.0;

            var s = new Scheme();
            s.Dark = dark;
            s.Source = source;
            s.Hue = h;
            s.Primary = new TonalPalette(h, 48.0);
            s.Secondary = new TonalPalette(h, 16.0);
            s.Tertiary = new TonalPalette(h + 60.0, 24.0);
            s.Neutral = new TonalPalette(h, 4.0);
            s.NeutralVariant = new TonalPalette(h, 8.0);
            s.Error = new TonalPalette(25.0, 84.0);
            s.Assign(contrast);
            return s;
        }

        private int _contrast = 0;

        private int Adj(int baseTone, int contrast, bool dark)
        {
            if (contrast <= 0) return baseTone;
            int step = contrast * (dark ? 2 : 3);
            if (dark) return baseTone > 15 ? Math.Min(100, baseTone + step) : baseTone;
            return baseTone < 90 ? Math.Min(100, baseTone + step) : baseTone;
        }

        private void Assign(int contrast)
        {
            _contrast = contrast;
            bool d = Dark;
            int nSurf = d ? Adj(6, contrast, true) : Adj(98, contrast, false);
            int nSurfHigh = d ? Adj(22, contrast, true) : Adj(92, contrast, false);
            int nSurfHigh2 = d ? Adj(17, contrast, true) : Adj(94, contrast, false);
            int nSurfLow = d ? Adj(10, contrast, true) : Adj(96, contrast, false);
            int nOnSurf = d ? Adj(90, contrast, true) : Adj(10, contrast, false);

            if (d)
            {
                OnPrimary = Primary.Tone(20); PrimaryContainer = Primary.Tone(30); OnPrimaryContainer = Primary.Tone(90);
                OnSecondary = Secondary.Tone(20); SecondaryContainer = Secondary.Tone(30); OnSecondaryContainer = Secondary.Tone(90);
                OnTertiary = Tertiary.Tone(20); TertiaryContainer = Tertiary.Tone(30); OnTertiaryContainer = Tertiary.Tone(90);
                OnError = Error.Tone(20); ErrorContainer = Error.Tone(30); OnErrorContainer = Error.Tone(90);
                Surface = Neutral.Tone(nSurf);
                OnSurface = Neutral.Tone(nOnSurf);
                Background = Surface;
                OnBackground = OnSurface;
                SurfaceVariant = NeutralVariant.Tone(30);
                OnSurfaceVariant = NeutralVariant.Tone(80);
                Outline = NeutralVariant.Tone(60);
                OutlineVariant = NeutralVariant.Tone(30);
                SurfaceLowest = Neutral.Tone(4);
                SurfaceLow = Neutral.Tone(nSurfLow);
                SurfaceMid = Neutral.Tone(12);
                SurfaceHigh = Neutral.Tone(nSurfHigh2);
                SurfaceHighest = Neutral.Tone(nSurfHigh);
                InverseSurface = Neutral.Tone(20);
                InverseOnSurface = Neutral.Tone(95);
                InversePrimary = Primary.Tone(80);
                Scrim = Color.FromArgb(0, 0, 0);
                Shadow = Color.FromArgb(0, 0, 0);
            }
            else
            {
                OnPrimary = Color.White; PrimaryContainer = Primary.Tone(90); OnPrimaryContainer = Primary.Tone(10);
                OnSecondary = Color.White; SecondaryContainer = Secondary.Tone(90); OnSecondaryContainer = Secondary.Tone(10);
                OnTertiary = Color.White; TertiaryContainer = Tertiary.Tone(90); OnTertiaryContainer = Tertiary.Tone(10);
                OnError = Color.White; ErrorContainer = Error.Tone(90); OnErrorContainer = Error.Tone(10);
                Surface = Neutral.Tone(nSurf);
                OnSurface = Neutral.Tone(nOnSurf);
                Background = Surface;
                OnBackground = OnSurface;
                SurfaceVariant = NeutralVariant.Tone(90);
                OnSurfaceVariant = NeutralVariant.Tone(30);
                Outline = NeutralVariant.Tone(50);
                OutlineVariant = NeutralVariant.Tone(80);
                SurfaceLowest = Color.White;
                SurfaceLow = Neutral.Tone(nSurfLow);
                SurfaceMid = Color.White;
                SurfaceHigh = Neutral.Tone(nSurfHigh2);
                SurfaceHighest = Neutral.Tone(nSurfHigh);
                InverseSurface = Neutral.Tone(20);
                InverseOnSurface = Neutral.Tone(95);
                InversePrimary = Primary.Tone(40);
                Scrim = Color.FromArgb(0, 0, 0);
                Shadow = Color.FromArgb(0, 0, 0);
            }
        }

        

        public Color Accent() { return Dark ? Primary.Tone(80) : Primary.Tone(40); }
        public Color AccentContainer() { return Dark ? Primary.Tone(30) : Primary.Tone(90); }
        public Color OnAccentContainer() { return Dark ? Primary.Tone(90) : Primary.Tone(10); }

        public static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static Color WithAlpha(Color c, int alpha)
        {
            return Color.FromArgb(alpha, c.R, c.G, c.B);
        }
    }
}