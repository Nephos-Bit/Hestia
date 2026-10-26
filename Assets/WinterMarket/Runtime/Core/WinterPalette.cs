using UnityEngine;

namespace WinterMarket
{
    public static class WinterPalette
    {
        public static readonly Color Snow = Hex("F5F7F8");
        public static readonly Color SnowShadow = Hex("DFE8ED");
        public static readonly Color Background = Hex("FAFBFC");
        public static readonly Color LightWood = Hex("B58C6B");
        public static readonly Color DarkWood = Hex("795A47");
        public static readonly Color ChristmasRed = Hex("A85D55");
        public static readonly Color FirGreen = Hex("557665");
        public static readonly Color Lantern = Hex("FFD19A");
        public static readonly Color Night = Hex("778CA8");
        public static readonly Color Path = Hex("E7ECEF");
        public static readonly Color RoofBlue = Hex("71879A");
        public static readonly Color CoatBlue = Hex("65798B");
        public static readonly Color CoatOchre = Hex("A17D5F");
        public static readonly Color Skin = Hex("D8AE91");

        public static Color Hex(string value)
        {
            return ColorUtility.TryParseHtmlString("#" + value, out Color color) ? color : Color.magenta;
        }
    }
}
