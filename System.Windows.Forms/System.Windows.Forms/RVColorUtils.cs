// CielColorLibrary.cs
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ColorUtils
{
    #region Enums

    /// <summary>
    /// Specifies the direction of a linear gradient.
    /// </summary>
    public enum GradientDirection
    {
        Horizontal,
        Vertical,
        ForwardDiagonal,
        BackwardDiagonal
    }

    /// <summary>
    /// Specifies the method for generating random colors for a gradient.
    /// </summary>
    public enum RandomColorMode
    {
        /// <summary>
        /// All colors in the gradient will share the same random Lightness (brightness) and Chroma (saturation).
        /// Only the Hue will vary.
        /// </summary>
        EquiSat,
        /// <summary>
        /// All colors in the gradient will share the same random Lightness (brightness).
        /// Both Chroma and Hue will vary.
        /// </summary>
        EquiBright
    }

    #endregion

    #region Color Space Structs

    /// <summary>
    /// Represents a color in the CIELAB color space.
    /// L* is the lightness (0 to 100), a* and b* are the color-opponent dimensions.
    /// </summary>
    public readonly struct CielLab
    {
        public readonly double L;
        public readonly double A;
        public readonly double B;
        public CielLab(double l, double a, double b) => (L, A, B) = (l, a, b);
    }

    #endregion

    /// <summary>
    /// A fast and efficient library for generating random colors and gradients
    /// in perceptually uniform color spaces (CIELAB).
    /// </summary>
    public static class CielColorGenerator
    {
        #region Constants

        // D65 standard illuminant reference white point
        private const double Xn = 0.95047;
        private const double Yn = 1.00000;
        private const double Zn = 1.08883;

        #endregion

        #region Core Conversion Functions (XYZ <-> RGB)

        private static Color RgbFromXyz(double x, double y, double z)
        {
            // sRGB to XYZ matrix inversion
            double r = x * 3.2404542 + y * -1.5371385 + z * -0.4985314;
            double g = x * -0.9692660 + y * 1.8760108 + z * 0.0415560;
            double b = x * 0.0556434 + y * -0.2040259 + z * 1.0572252;

            // Apply gamma correction
            r = r <= 0.0031308 ? 12.92 * r : 1.055 * Math.Pow(r, 1.0 / 2.4) - 0.055;
            g = g <= 0.0031308 ? 12.92 * g : 1.055 * Math.Pow(g, 1.0 / 2.4) - 0.055;
            b = b <= 0.0031308 ? 12.92 * b : 1.055 * Math.Pow(b, 1.0 / 2.4) - 0.055;

            // Clamp to [0, 1] and convert to byte [0, 255]
            byte rByte = ToClampedByte(r);
            byte gByte = ToClampedByte(g);
            byte bByte = ToClampedByte(b);

            return Color.FromArgb(rByte, gByte, bByte);
        }

        private static (double X, double Y, double Z) XyzFromRgb(int r, int g, int b)
        {
            double rLin = r / 255.0;
            double gLin = g / 255.0;
            double bLin = b / 255.0;

            rLin = rLin <= 0.04045 ? rLin / 12.92 : Math.Pow((rLin + 0.055) / 1.055, 2.4);
            gLin = gLin <= 0.04045 ? gLin / 12.92 : Math.Pow((gLin + 0.055) / 1.055, 2.4);
            bLin = bLin <= 0.04045 ? bLin / 12.92 : Math.Pow((bLin + 0.055) / 1.055, 2.4);

            double x = rLin * 0.4124564 + gLin * 0.3575761 + bLin * 0.1804375;
            double y = rLin * 0.2126729 + gLin * 0.7151522 + bLin * 0.0721750;
            double z = rLin * 0.0193339 + gLin * 0.1191920 + bLin * 0.9503041;

            return (x, y, z);
        }

        private static byte ToClampedByte(double value)
        {
            if (value <= 0) return 0;
            if (value >= 1) return 255;
            return (byte)(value * 255.0 + 0.5);
        }

        #endregion

        #region CIELAB Conversions

        public static Color RgbFromLab(CielLab lab)
        {
            double fy = (lab.L + 16.0) / 116.0;
            double fx = lab.A / 500.0 + fy;
            double fz = fy - lab.B / 200.0;

            double x = Xn * (Math.Pow(fx, 3) > 0.008856 ? Math.Pow(fx, 3) : (116.0 * fx - 16.0) / 7.787);
            double y = Yn * (lab.L > 7.9996 ? Math.Pow(fy, 3) : lab.L / 903.3);
            double z = Zn * (Math.Pow(fz, 3) > 0.008856 ? Math.Pow(fz, 3) : (116.0 * fz - 16.0) / 7.787);

            return RgbFromXyz(x, y, z);
        }

        public static CielLab LabFromRgb(Color color)
        {
            var (x, y, z) = XyzFromRgb(color.R, color.G, color.B);

            double fx = x / Xn;
            double fy = y / Yn;
            double fz = z / Zn;

            fx = fx > 0.008856 ? Math.Pow(fx, 1.0 / 3.0) : (7.787 * fx + 16.0) / 116.0;
            fy = fy > 0.008856 ? Math.Pow(fy, 1.0 / 3.0) : (7.787 * fy + 16.0) / 116.0;
            fz = fz > 0.008856 ? Math.Pow(fz, 1.0 / 3.0) : (7.787 * fz + 16.0) / 116.0;

            double l = 116.0 * fy - 16.0;
            double a = 500.0 * (fx - fy);
            double b = 200.0 * (fy - fz);

            return new CielLab(l, a, b);
        }

        #endregion

        #region Random Color Generation Helpers

        /// <summary>
        /// Generates a random color with a specific lightness (brightness) and chroma (intensity/saturation).
        /// The hue is randomized.
        /// </summary>
        /// <param name="lightness">The perceived brightness (0.0 for black, 100.0 for white).</param>
        /// <param name="chroma">The colorfulness/intensity (0.0 for grayscale, ~100 for highly saturated).</param>
        /// <returns>A random System.Drawing.Color with the specified lightness and chroma.</returns>
        public static Color RandomColorWithLightnessAndChroma(double lightness, double chroma)
        {
            if (lightness < 0 || lightness > 100) throw new ArgumentOutOfRangeException(nameof(lightness), "Lightness must be between 0 and 100.");
            if (chroma < 0) throw new ArgumentOutOfRangeException(nameof(chroma), "Chroma must be non-negative.");

            double l = lightness;
            double c = chroma;
            double h = Random.Shared.NextDouble() * 360.0;

            double hRad = h * Math.PI / 180.0;
            double a = c * Math.Cos(hRad);
            double b = c * Math.Sin(hRad);

            return RgbFromLab(new CielLab(l, a, b));
        }

        /// <summary>
        /// Generates a random color with a specific lightness (brightness).
        /// The hue and chroma (intensity/saturation) are randomized.
        /// </summary>
        /// <param name="lightness">The perceived brightness (0.0 for black, 100.0 for white).</param>
        /// <returns>A random System.Drawing.Color with the specified lightness.</returns>
        public static Color RandomColorWithLightness(double lightness)
        {
            if (lightness < 0 || lightness > 100) throw new ArgumentOutOfRangeException(nameof(lightness), "Lightness must be between 0 and 100.");

            const double max_ab = 128.0; // Safe range for a* and b* to stay within sRGB gamut
            double l = lightness;
            double a = (Random.Shared.NextDouble() - 0.5) * 2.0 * max_ab;
            double b = (Random.Shared.NextDouble() - 0.5) * 2.0 * max_ab;

            return RgbFromLab(new CielLab(l, a, b));
        }

        #endregion
    }

    /// <summary>
    /// Generates linear gradients by interpolating in the CIELAB color space.
    /// </summary>
    public static class CielGradientGenerator
    {
        /// <summary>
        /// Creates a LinearGradientBrush with perceptually uniform color interpolation from an array of colors.
        /// </summary>
        /// <param name="colors">The array of colors to form the gradient. Must contain at least 2 colors.</param>
        /// <param name="direction">The direction of the gradient.</param>
        /// <returns>A LinearGradientBrush with CIELAB-interpolated colors.</returns>
        public static LinearGradientBrush GenerateLinearGradient(Color[] colors, GradientDirection direction)
        {
            if (colors == null || colors.Length < 2)
            {
                throw new ArgumentException("Colors array must contain at least 2 colors.", nameof(colors));
            }

            // 1. Convert all colors to CIELAB
            CielLab[] labColors = new CielLab[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                labColors[i] = CielColorGenerator.LabFromRgb(colors[i]);
            }

            // 2. Create the brush with the first and last colors as the endpoints
            var brush = new LinearGradientBrush(new Rectangle(0, 0, 400, 400), colors[0], colors[^1], GetLinearGradientMode(direction)) { GammaCorrection = true};

            // 3. Use a ColorBlend to force the brush to use our calculated colors
            var colorBlend = new ColorBlend();
            colorBlend.Colors = colors;
            colorBlend.Positions = new float[colors.Length];

            for (int i = 0; i < colors.Length; i++)
            {
                colorBlend.Positions[i] = (float)i / (colors.Length - 1);
            }
            brush.WrapMode = WrapMode.TileFlipXY;
            brush.InterpolationColors = colorBlend;
            return brush;
        }

        private static LinearGradientMode GetLinearGradientMode(GradientDirection direction)
        {
            return direction switch
            {
                GradientDirection.Vertical => LinearGradientMode.Vertical,
                GradientDirection.ForwardDiagonal => LinearGradientMode.ForwardDiagonal,
                GradientDirection.BackwardDiagonal => LinearGradientMode.BackwardDiagonal,
                _ => LinearGradientMode.Horizontal,
            };
        }
    }

    /// <summary>
    /// Generates random linear gradients using CIELAB color space for perceptual uniformity.
    /// </summary>
    public static class CielRandomGradientGenerator
    {
        /// <summary>
        /// Generates a random gradient brush with default settings (3 colors, EquiSat mode, Horizontal direction).
        /// </summary>
        /// <returns>A LinearGradientBrush with a random 3-color gradient.</returns>
        public static LinearGradientBrush Generate()
        {
            return Generate(3, RandomColorMode.EquiSat, GradientDirection.Horizontal);
        }

        /// <summary>
        /// Generates a random gradient brush with the specified number of colors and mode.
        /// </summary>
        /// <param name="numColors">The number of colors in the gradient (must be 2 or more).</param>
        /// <param name="mode">The mode for generating random colors (EquiSat or EquiBright).</param>
        /// <param name="direction">The direction of the gradient.</param>
        /// <returns>A LinearGradientBrush with a random multi-color gradient.</returns>
        public static LinearGradientBrush Generate(int numColors, RandomColorMode mode, GradientDirection direction = GradientDirection.Horizontal)
        {
            if (numColors < 2)
            {
                throw new ArgumentException("Number of colors must be at least 2.", nameof(numColors));
            }

            Color[] gradientColors = new Color[numColors];

            switch (mode)
            {
                case RandomColorMode.EquiSat:
                    // Pick one random lightness and one random chroma for the whole gradient
                    double randomLightness = Random.Shared.NextDouble() * 100.0;
                    double randomChroma = Random.Shared.NextDouble() * 80.0; // 80 is a safe, vibrant max for chroma
                    for (int i = 0; i < numColors; i++)
                    {
                        //gradientColors[i] = CielColorGenerator.RandomColorWithLightnessAndChroma(randomLightness, randomChroma);
                        gradientColors[i] = CielColorGenerator.RandomColorWithLightnessAndChroma(80, randomChroma);
                    }
                    break;

                case RandomColorMode.EquiBright:
                    // Pick one random lightness for the whole gradient
                    double commonLightness = Random.Shared.NextDouble() * 100.0;
                    for (int i = 0; i < numColors; i++)
                    {
                        //gradientColors[i] = CielColorGenerator.RandomColorWithLightness(commonLightness);
                        gradientColors[i] = CielColorGenerator.RandomColorWithLightness(30);
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), "Unsupported random color mode.");
            }

            return CielGradientGenerator.GenerateLinearGradient(gradientColors, direction);
        }
    }
}