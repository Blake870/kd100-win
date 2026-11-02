using System.Globalization;

namespace Kd100Win;

/// <summary>Tray and notification texts in English and Russian. The settings window has its own copy in settings.html.</summary>
internal static class Strings
{
    static bool _ru = Resolve("auto") == "ru";

    /// <summary>"auto" follows the Windows display language.</summary>
    public static string Resolve(string language) =>
        language == "auto"
            ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en"
            : language;

    public static void SetLanguage(string language) => _ru = Resolve(language) == "ru";

    static string T(string en, string ru) => _ru ? ru : en;

    public static string Connected => T("KD100 connected", "KD100 подключён");
    public static string NotConnected => T("KD100 not connected", "KD100 не подключён");
    public static string Profile => T("Profile", "Профиль");
    public static string DialMode => T("Dial mode", "Режим колеса");
    public static string Settings => T("Settings…", "Настройки…");
    public static string Monitor => T("Input monitor…", "Монитор нажатий…");
    public static string EditJson => T("Edit config file…", "Открыть файл конфигурации…");
    public static string Reload => T("Reload config", "Перечитать конфигурацию");
    public static string OpenFolder => T("Open config folder", "Открыть папку конфигурации");
    public static string Autostart => T("Start with Windows", "Запускать вместе с Windows");
    public static string Pause => T("Pause", "Пауза");
    public static string Exit => T("Exit", "Выход");
    public static string Paused => T("paused", "пауза");
    public static string NotFoundTitle => T("KD100 not found", "KD100 не найден");
    public static string NotFoundText => T("Plug in the cable or the 2.4 GHz receiver; it will connect automatically.",
        "Подключите кабель или приёмник 2.4 ГГц — устройство подхватится автоматически.");
    public static string ConfigErrorTitle => T("Config error, using the built-in defaults", "Ошибка в конфигурации, используются настройки по умолчанию");
    public static string ConfigNotReloaded => T("Config not reloaded: fix the error and save again", "Конфигурация не применена: исправьте ошибку и сохраните снова");
    public static string ConfigReloaded => T("Config reloaded", "Конфигурация применена");
    public static string HuionTitle => T("Huion software is running", "Запущена программа Huion");
    public static string HuionText(string names) => T(
        $"{names} also handles the KD100, so buttons may fire twice. Quit it from its tray icon.",
        $"{names} тоже обрабатывает KD100, кнопки могут срабатывать дважды. Закройте её через значок в трее.");
    public static string AutostartFailed => T("Could not change autostart", "Не удалось изменить автозапуск");
    public static string AlreadyRunning => T("kd100-win is already running (see the tray icon).", "kd100-win уже запущен (значок в трее).");
    public static string SaveChanges => T("Save changes before closing?", "Сохранить изменения перед закрытием?");
    public static string WebView2Missing => T(
        "The settings window needs the Microsoft Edge WebView2 Runtime. Install it from https://go.microsoft.com/fwlink/p/?LinkId=2124703 or edit the config file directly.",
        "Для окна настроек нужен Microsoft Edge WebView2 Runtime. Установите его с https://go.microsoft.com/fwlink/p/?LinkId=2124703 или редактируйте файл конфигурации вручную.");
}
