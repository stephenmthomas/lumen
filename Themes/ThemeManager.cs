using System;
using System.Windows;

namespace DisplayControl
{
    /// <summary>
    /// Manages runtime theme switching.
    /// 
    /// The theme dictionary is always the FIRST entry (index 0) in
    /// Application.Resources.MergedDictionaries. Every style file
    /// references theme tokens via DynamicResource, so swapping
    /// the dictionary at index 0 updates the entire UI instantly.
    /// 
    /// Usage:
    ///   ThemeManager.ApplyTheme("Styles/ThemeDark.xaml");
    ///   ThemeManager.ApplyTheme("Styles/ThemeLight.xaml");
    ///   ThemeManager.ApplyTheme("Styles/ThemeNord.xaml");
    /// 
    /// You can also override individual tokens at runtime:
    ///   ThemeManager.SetColor("Theme.Accent.Base", "#FF6B2A");
    ///   ThemeManager.SetCornerRadius("Theme.Radius.Large", new CornerRadius(12));
    /// </summary>
    public static class ThemeManager
    {
        private const int ThemeDictionaryIndex = 0;

        /// <summary>
        /// Current theme file path (relative to the application).
        /// </summary>
        public static string CurrentTheme { get; private set; } = "Styles/ThemeDark.xaml";

        /// <summary>
        /// Replace the entire theme dictionary at runtime.
        /// </summary>
        /// <param name="relativeUri">
        /// Path to the theme XAML file, e.g. "Styles/ThemeLight.xaml"
        /// </param>
        public static void ApplyTheme(string relativeUri)
        {
            var dictionaries = Application.Current.Resources.MergedDictionaries;

            // Build the new dictionary
            var newTheme = new ResourceDictionary
            {
                Source = new Uri(relativeUri, UriKind.Relative)
            };

            // Swap: remove old, insert new at the same index
            if (dictionaries.Count > ThemeDictionaryIndex)
                dictionaries.RemoveAt(ThemeDictionaryIndex);

            dictionaries.Insert(ThemeDictionaryIndex, newTheme);
            CurrentTheme = relativeUri;
        }

        /// <summary>
        /// Override a single Color token in the current theme.
        /// Useful for accent-color pickers or per-user customization.
        /// </summary>
        public static void SetColor(string key, string hex)
        {
            var color = (System.Windows.Media.Color)
                System.Windows.Media.ColorConverter.ConvertFromString(hex);

            var dict = Application.Current.Resources.MergedDictionaries[ThemeDictionaryIndex];
            dict[key] = color;

            // Also update the matching brush if it exists
            string brushKey = key.Replace("Theme.", "Theme.Brush.");
            if (dict.Contains(brushKey))
            {
                dict[brushKey] = new System.Windows.Media.SolidColorBrush(color);
            }
        }

        /// <summary>
        /// Override a single CornerRadius token.
        /// </summary>
        public static void SetCornerRadius(string key, CornerRadius radius)
        {
            var dict = Application.Current.Resources.MergedDictionaries[ThemeDictionaryIndex];
            dict[key] = radius;
        }

        /// <summary>
        /// Override a single Thickness token (border thickness).
        /// </summary>
        public static void SetThickness(string key, Thickness thickness)
        {
            var dict = Application.Current.Resources.MergedDictionaries[ThemeDictionaryIndex];
            dict[key] = thickness;
        }

        /// <summary>
        /// Override a single Double token (font size, etc).
        /// </summary>
        public static void SetDouble(string key, double value)
        {
            var dict = Application.Current.Resources.MergedDictionaries[ThemeDictionaryIndex];
            dict[key] = value;
        }
    }
}
