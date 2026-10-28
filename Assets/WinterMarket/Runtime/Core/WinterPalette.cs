using UnityEngine;

namespace WinterMarket
{
    public static class WinterPalette
    {
        // The palette deliberately separates lit snow, compressed snow and roof snow. The
        // former values were so close together on a phone display that the voxel layers read
        // as a single flat white surface.
        public static readonly Color Snow = Hex("F1F5F7");
        public static readonly Color SnowShadow = Hex("C9DAE4");
        public static readonly Color SnowCap = Hex("BCD4E2");
        public static readonly Color SnowCapShade = Hex("7FA5BB");
        public static readonly Color Background = Hex("E8F0F4");
        public static readonly Color LightWood = Hex("BC916A");
        public static readonly Color DarkWood = Hex("60493B");
        public static readonly Color ChristmasRed = Hex("A9564E");
        public static readonly Color FirGreen = Hex("4F705F");
        public static readonly Color ForestFirGreen = Hex("3E6253");
        public static readonly Color Lantern = Hex("FFD19A");
        public static readonly Color Night = Hex("778CA8");
        // Cleared, compacted ground: deliberately earthy against the surrounding snow.
        public static readonly Color Path = Hex("6B5846");
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
