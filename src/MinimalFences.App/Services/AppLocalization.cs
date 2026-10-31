using System;
using System.ComponentModel;
using MinimalFences.Core;

namespace MinimalFences.Services;

internal sealed class AppLocalization : ObservableModel
{
    private static AppLocalization? _current;
    public static AppLocalization Current => _current ??= new AppLocalization();

    private string _lang = "ru";
    public string Language
    {
        get => _lang;
        set
        {
            string clean = (value ?? "ru").ToLowerInvariant().StartsWith("en") ? "en" : "ru";
            if (_lang == clean) return;
            _lang = clean;
            OnAllPropertiesChanged();
        }
    }

    private void OnAllPropertiesChanged()
    {
        var handler = typeof(ObservableModel).GetField("PropertyChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(this) as PropertyChangedEventHandler;
        handler?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public bool IsRussian => _lang == "ru";

    public string AppTitle => "Minimal Fences";
    public string AppSubtitle => IsRussian ? "Порядок на рабочем столе. Больше концентрации." : "A little order. More room to focus.";
    public string CreateGroup => IsRussian ? "+  Создать группу" : "+  Create group";
    public string YourGroups => IsRussian ? "ВАШИ ГРУППЫ" : "YOUR GROUPS";
    public string ShowHideAll => IsRussian ? "Показать / скрыть все" : "Show / hide all";

    public string TabSelectedGroup => IsRussian ? "Выбранная группа" : "Selected group";
    public string TabGeneral => IsRussian ? "Основные" : "General";
    public string TabDefaults => IsRussian ? "Оформление новых групп" : "New group defaults";

    public string NoGroupSelected => IsRussian ? "Создайте или выберите группу для начала." : "Create or select a group to get started.";
    public string GroupNameLabel => IsRussian ? "Название группы" : "Group name";
    public string AutoSaveHint => IsRussian ? "Изменения сохраняются автоматически и отображаются на рабочем столе." : "Changes are saved automatically and shown on your desktop.";
    public string ItemsCountFormat(int count) => IsRussian ? $"{count} элементов в этой группе" : $"{count} items in this group";
    public string AddFiles => IsRussian ? "Добавить файлы…" : "Add files…";
    public string AddFolder => IsRussian ? "Добавить папку…" : "Add folder…";
    public string DeleteGroup => IsRussian ? "Удалить группу…" : "Delete group…";
    public string ReorderHint => IsRussian ? "Перетаскивайте значки для изменения порядка. Правый клик — дополнительные действия." : "Drag icons to reorder them or move them between groups. Right-click an icon for more options.";

    public string BackgroundColor => IsRussian ? "Цвет фона" : "Background color";
    public string CustomColor => IsRussian ? "Выбрать свой…" : "Custom…";
    public string BackgroundOpacity => IsRussian ? "Непрозрачность фона" : "Background opacity";
    public string OpacityHint => IsRussian ? "0% — полностью прозрачно, 100% — сплошной фон." : "0% — fully transparent, 100% — solid background.";
    public string Corners => IsRussian ? "Скругление" : "Corners";
    public string IconSize => IsRussian ? "Размер значков" : "Icon size";
    public string GroupOrientationLabel => IsRussian ? "Ориентация группы" : "Group orientation";
    public string LayoutLabel => IsRussian ? "Вид расположения (сетка / вертикально / список)" : "Icon layout";
    public string IconsOnlyLabel => IsRussian ? "Только значки (режим без подписей)" : "Icons only mode (hide labels)";
    public string SubtleBorder => IsRussian ? "Тонкая рамка" : "Subtle border";
    public string ShowTitle => IsRussian ? "Показывать заголовок" : "Show group title";
    public string HideHeaderWhenCollapsed => IsRussian ? "Скрывать всё в свернутом виде" : "Hide everything when collapsed";
    public string HideHeaderWhenCollapsedHint => IsRussian ? "В свернутом виде скрывает заголовок, значок и кнопки. При разворачивании они появляются." : "In collapsed state, hides title, badge, and buttons. When expanded, they appear.";
    public string TitleColor => IsRussian ? "Цвет заголовка и текста…" : "Title & label color…";
    public string TitleSize => IsRussian ? "Размер заголовка" : "Title size";

    public string MakeDesktopYours => IsRussian ? "Настройте рабочий стол" : "Make the desktop yours";
    public string StartWithWindows => IsRussian ? "Запускать вместе с Windows" : "Start with Windows";
    public string DesktopDoubleClick => IsRussian ? "Двойной клик по пустому столу для скрытия/показа" : "Double-click empty desktop to show / hide groups";
    public string DesktopDoubleClickHint => IsRussian ? "Скрываются только группы. Обычные значки рабочего стола остаются." : "Only your groups are hidden. Ordinary desktop icons stay visible.";
    public string EnableHotkey => IsRussian ? "Включить сочетание клавиш" : "Enable keyboard shortcut";
    public string HotkeyLabel => IsRussian ? "Горячая клавиша скрытия/показа" : "Show / hide shortcut";
    public string HotkeyHint => IsRussian ? "Нажмите на поле и нажмите нужное сочетание клавиш (например: Ctrl+Alt+F)" : "Click the field and press the desired shortcut (e.g. Ctrl+Alt+F)";
    public string HotkeyPressPrompt => IsRussian ? "Нажмите клавиши на клавиатуре…" : "Press keys on keyboard…";
    public string LanguageLabel => IsRussian ? "Язык интерфейса" : "Language";
    public string LanguageRussian => "Русский";
    public string LanguageEnglish => "English";
    public string ThemeLabel => IsRussian ? "Тема оформления" : "Theme";
    public string ThemeDark => IsRussian ? "Тёмная" : "Dark";
    public string ThemeLight => IsRussian ? "Светлая" : "Light";

    public string DesktopRecovery => IsRussian ? "Восстановление рабочего стола" : "Desktop recovery";
    public string RestoreGroups => IsRussian ? "Вернуть группы на экран" : "Restore groups to primary screen";
    public string ReconnectDesktop => IsRussian ? "Переподключиться к рабочему столу" : "Reconnect to desktop";
    public string RecoveryHint => IsRussian ? "Используйте эти действия при смене мониторов или если группы перестали отображаться." : "Use these actions after changing monitors or if Explorer stops displaying your groups.";
    public string OpenDataFolder => IsRussian ? "Открыть папку настроек и журналов" : "Open settings & logs folder";
    public string StartWithStyle => IsRussian ? "Создайте свой стиль" : "Start with your style";
    public string DefaultsHint => IsRussian ? "Эти настройки будут применяться к новым группам." : "These settings apply to groups you create next.";
    public string StatusHint => IsRussian ? "Работает в системном трее. Закрытие этого окна оставляет группы открытыми." : "Runs quietly in your system tray. Closing this window keeps your groups open.";

    public string EmptyHint => IsRussian ? "Перетащите ярлыки, файлы или папки сюда" : "Drop shortcuts, files or folders here";
    public string GroupOptionsTooltip => IsRussian ? "Параметры группы" : "Group options";
    public string RollUpGroup => IsRussian ? "Свернуть группу" : "Roll up group";
    public string UnrollGroup => IsRussian ? "Развернуть группу" : "Expand group";
    public string UnrollGroupRight => IsRussian ? "Развернуть группу вправо" : "Expand group to the right";
    public string RollUpGroupLeft => IsRussian ? "Свернуть группу влево" : "Collapse group to the left";
    public string UnrollGroupLeft => IsRussian ? "Развернуть группу влево" : "Expand group to the left";
    public string RollUpGroupRight => IsRussian ? "Свернуть группу вправо" : "Collapse group to the right";
    public string DarkGrayBackground => IsRussian ? "Тёмно-серый" : "Dark gray";
    public string MinimizeTooltip => IsRussian ? "Свернуть" : "Minimize";
    public string MaximizeTooltip => IsRussian ? "Развернуть" : "Maximize";
    public string RestoreTooltip => IsRussian ? "Восстановить" : "Restore";
    public string CloseTooltip => IsRussian ? "Закрыть" : "Close";

    public string TitleFontLabel => IsRussian ? "Шрифт заголовка" : "Title font";
    public string ItemFontLabel => IsRussian ? "Шрифт подписей значков" : "Icon labels font";
    public string BadgeStyleLabel => IsRussian ? "Значок у заголовка" : "Title badge icon";
    public string BadgeStyleIcons => IsRussian ? "Значки" : "Icons";
    public string BadgePickerTitle => IsRussian ? "Выбор значка" : "Select badge icon";
    public string SearchIconPlaceholder => IsRussian ? "Поиск значка…" : "Search icon…";
    public string BadgeColorLabel => IsRussian ? "Цвет значка" : "Badge color";
    public string BadgeStyleDot => IsRussian ? "Точка" : "Dot";
    public string BadgeStyleSquare => IsRussian ? "Квадрат" : "Square";
    public string BadgeStyleTriangle => IsRussian ? "Треугольник" : "Triangle";
    public string BadgeStyleStar => IsRussian ? "Звезда" : "Star";
    public string BadgeStyleHeart => IsRussian ? "Сердце" : "Heart";
    public string BadgeStyleSparkle => IsRussian ? "Искорка" : "Sparkle";
    public string BadgeStyleImage => IsRussian ? "Своя картинка…" : "Custom image…";
    public string ChooseBadgeImageTitle => IsRussian ? "Выберите изображение значка" : "Select badge image";
    public string ImageFilesFilter => IsRussian ? "Изображения (*.png;*.jpg;*.jpeg;*.ico;*.bmp)|*.png;*.jpg;*.jpeg;*.ico;*.bmp|Все файлы|*.*" : "Images (*.png;*.jpg;*.jpeg;*.ico;*.bmp)|*.png;*.jpg;*.jpeg;*.ico;*.bmp|All files|*.*";
    public string CropImageTitle => IsRussian ? "Обрезка изображения значка" : "Crop badge image";
    public string CropImageZoom => IsRussian ? "Масштаб" : "Zoom";
    public string CropImagePanHint => IsRussian ? "Перетаскивайте изображение мышью для позиционирования" : "Drag the image to position the crop area";

    public string ColorPickerTitle => IsRussian ? "Выбор цвета" : "Select Color";
    public string ColorWheel => IsRussian ? "Цветовой круг" : "Color Wheel";
    public string ColorBrightness => IsRussian ? "Яркость" : "Brightness";
    public string ColorPreview => IsRussian ? "Предпросмотр" : "Preview";
    public string ColorHex => IsRussian ? "HEX код" : "HEX code";
    public string ColorRed => IsRussian ? "Красный (R)" : "Red (R)";
    public string ColorGreen => IsRussian ? "Зелёный (G)" : "Green (G)";
    public string ColorBlue => IsRussian ? "Синий (B)" : "Blue (B)";
    public string ColorApply => IsRussian ? "Применить" : "Apply";
    public string ColorCancel => IsRussian ? "Отмена" : "Cancel";
    public string ColorPalettePresets => IsRussian ? "Палитра оттенков" : "Color Palette";

    public string MenuOpen => IsRussian ? "Открыть" : "Open";
    public string MenuOpenLocation => IsRussian ? "Открыть расположение файла" : "Open file location";
    public string MenuMoveToGroup => IsRussian ? "Переместить в группу" : "Move to group";
    public string MenuMoveEarlier => IsRussian ? "Переместить раньше" : "Move earlier";
    public string MenuRemoveFromGroup => IsRussian ? "Убрать из группы" : "Remove from group";
    public string MenuRestoreToDesktop => IsRussian ? "Вернуть на рабочий стол" : "Restore to desktop";
    public string MenuCustomize => IsRussian ? "Переименовать и настроить…" : "Rename & customize…";
    public string ShowLabelsAction => IsRussian ? "Показывать подписи значков" : "Show icon labels";
    public string HideLabelsAction => IsRussian ? "Только значки (скрыть подписи)" : "Icons only (hide labels)";
    public string SetVerticalAction => IsRussian ? "Вертикальный столбик (разворот вправо)" : "Vertical pillar (expand right)";
    public string SetVerticalLeftAction => IsRussian ? "Вертикальный столбик (разворот влево)" : "Vertical pillar (expand left)";
    public string SetHorizontalAction => IsRussian ? "Горизонтальная группа (разворот вниз)" : "Horizontal fence (expand down)";
    public string MenuAddFiles => IsRussian ? "Добавить файлы…" : "Add files…";
    public string MenuAddFolder => IsRussian ? "Добавить папку…" : "Add folder…";
    public string MenuCreateGroup => IsRussian ? "Создать группу" : "Create group";
    public string MenuHideAll => IsRussian ? "Скрыть все группы" : "Hide all groups";
    public string MenuShowAll => IsRussian ? "Показать все группы" : "Show all groups";
    public string MenuDeleteGroup => IsRussian ? "Удалить группу…" : "Delete group…";
    public string DeleteGroupConfirm(string name) => IsRussian
        ? $"Удалить «{name}»?\n\nБудет удалена только группа и ссылки. Сами файлы останутся на диске."
        : $"Delete “{name}”?\n\nOnly this group and its references will be removed. Original files will stay where they are.";
    public string DeleteGroupTitle => IsRussian ? "Удалить группу" : "Delete group";

    public string TrayHide => IsRussian ? "Скрыть группы" : "Hide groups";
    public string TrayShow => IsRussian ? "Показать группы" : "Show groups";
    public string TrayCreate => IsRussian ? "Создать новую группу" : "Create new group";
    public string TraySettings => IsRussian ? "Настройки" : "Settings";
    public string TrayStartup => IsRussian ? "Запускать вместе с Windows" : "Start with Windows";
    public string TrayReconnect => IsRussian ? "Переподключиться к рабочему столу" : "Reconnect to desktop";
    public string TrayExit => IsRussian ? "Выход" : "Exit";

    public string ItemMissing => IsRussian ? "Элемент не найден. Восстановите исходный файл или удалите ссылку из группы." : "This item is missing. Restore the original file or remove its reference from the group.";
    public string LocationMissing => IsRussian ? "Исходный файл или папка больше не существует." : "The original file or folder no longer exists.";
    public string NoNewItemsAdded => IsRussian ? "Новые элементы не добавлены. Используйте существующие файлы или папки, которых ещё нет в этой группе." : "No new items were added. Use existing files or folders that are not already in this group.";
    public string AddFilesDialogTitle => IsRussian ? "Добавить файлы или ярлыки" : "Add references to files or shortcuts";
    public string AddFolderDialogTitle => IsRussian ? "Добавить папку" : "Add a folder reference";
    public string AllFilesFilter => IsRussian ? "Все файлы|*.*" : "All files|*.*";
    public string DoubleClickUnavailable => IsRussian ? "Обнаружение двойного клика по рабочему столу недоступно. Используйте значок в трее или горячую клавишу." : "Desktop double-click detection is unavailable. Use the tray or keyboard shortcut.";
    public string StartupUpdateError(string error) => IsRussian ? $"Не удалось обновить автозагрузку Windows: {error}" : $"Could not update Windows startup: {error}";
    public string SettingsSaveError => IsRussian ? "Не удалось сохранить настройки. Проверьте права доступа и свободное место на диске." : "Settings could not be saved. Check access and free space in the settings folder.";
}
