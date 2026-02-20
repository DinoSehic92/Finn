using Avalonia.Media;
using Avalonia;

namespace Finn.Services
{
    // Centralized UI defaults used by UIService and UI-related dialogs/viewmodels.
    public static class UIDefaults
    {
        public static readonly Color DefaultColor1 = Color.Parse("#1F2933"); // dark background
        public static readonly Color DefaultColor2 = Color.Parse("#0A84FF"); // dark accent
        public static readonly Color DefaultColor3 = Color.Parse("#E9EEF5"); // light background
        public static readonly Color DefaultColor4 = Color.Parse("#0066C0"); // light accent

        public const string DefaultFontName = "Roboto";
        public const int DefaultFontSize = 15;

        public const double DefaultCornerRadius = 10.0;
        public const bool DefaultCornerRadiusVal = true;
        public const bool DefaultShadowVal = false;
        public const bool DefaultDarkMode = true;
    }
}
