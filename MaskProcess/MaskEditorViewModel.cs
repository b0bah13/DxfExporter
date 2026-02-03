using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;

using DxfExporter.Constants;

namespace DxfExporter.MaskProcess
{
    /// <summary>
    /// Управляет всей логикой формирования маски выгрузки (AvailableHeaders + MaskParts + Drag&Drop + шаблоны).
    /// MainWindow должен только делегировать сюда события UI.
    /// </summary>
    public class MaskEditorViewModel : INotifyPropertyChanged
    {
        #region Fields

        private ObservableCollection<HeaderItem> _availableHeaders = new ObservableCollection<HeaderItem>();

        private ObservableCollection<MaskPart> _maskParts = new ObservableCollection<MaskPart>();

        private string _customText = string.Empty;

        private string _modeName = string.Empty;

        private Point _dragStartPoint;

        #endregion

        #region Properties

        /// <summary>
        /// Доступные заголовки (левый список).
        /// </summary>
        public ObservableCollection<HeaderItem> AvailableHeaders
        {
            get => _availableHeaders;
            set
            {
                _availableHeaders = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Текущая маска (правый список).
        /// </summary>
        public ObservableCollection<MaskPart> MaskParts
        {
            get => _maskParts;
            set
            {
                _maskParts = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Пользовательский текст для добавления в маску.
        /// </summary>
        public string CustomText
        {
            get => _customText;
            set
            {
                _customText = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Название выбранного режима/шаблона маски.
        /// </summary>
        public string ModeName
        {
            get => _modeName;
            set
            {
                _modeName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsMaskSettingsEnabled));
            }
        }

        /// <summary>
        /// Нужно ли разрешать редактирование маски (GroupBox настройки).
        /// </summary>
        public bool IsMaskSettingsEnabled => ModeName == MaskConst.Пользовательские;

        #endregion

        #region Ctor

        /// <summary>
        /// Создаёт менеджер маски и инициализирует доступные заголовки.
        /// </summary>
        public MaskEditorViewModel()
        {
            InitAvailableHeaders();
        }

        #endregion

        #region Public API

        /// <summary>
        /// Заполняет список доступных заголовков.
        /// </summary>
        public void InitAvailableHeaders()
        {
            AvailableHeaders.Clear();

            AvailableHeaders.Add(new HeaderItem(HeaderConst.Обозначение));
            AvailableHeaders.Add(new HeaderItem(HeaderConst.Наименование));
            AvailableHeaders.Add(new HeaderItem(HeaderConst.Материал));
            AvailableHeaders.Add(new HeaderItem(HeaderConst.Толщина));
            AvailableHeaders.Add(new HeaderItem(HeaderConst.Количество));
        }

        /// <summary>
        /// Применяет выбранный шаблон маски (Стандарт/Материал/Пользовательские).
        /// </summary>
        public void ApplyTemplate(string templateName)
        {
            if (string.IsNullOrWhiteSpace(templateName))
                return;

            ModeName = templateName;

            ClearMaskParts();

            switch (ModeName)
            {
                case MaskConst.Стандарт:
                    MaskParts.Add(new MaskPart($"{{{HeaderConst.Количество}}}"));
                    MaskParts.Add(new MaskPart(" шт_"));
                    MaskParts.Add(new MaskPart($"{{{HeaderConst.Обозначение}}}"));
                    MaskParts.Add(new MaskPart("_"));
                    MaskParts.Add(new MaskPart($"{{{HeaderConst.Наименование}}}"));
                    break;

                case MaskConst.Материал:
                    MaskParts.Add(new MaskPart($"{{{HeaderConst.Материал}}}"));
                    MaskParts.Add(new MaskPart("_S="));
                    MaskParts.Add(new MaskPart($"{{{HeaderConst.Толщина}}}"));
                    MaskParts.Add(new MaskPart("("));
                    MaskParts.Add(new MaskPart($"{{{HeaderConst.Количество}}}"));
                    MaskParts.Add(new MaskPart(" шт)_"));
                    MaskParts.Add(new MaskPart($"{{{HeaderConst.Обозначение}}}"));
                    MaskParts.Add(new MaskPart("_"));
                    MaskParts.Add(new MaskPart($"{{{HeaderConst.Наименование}}}"));
                    break;

                case MaskConst.Пользовательские:
                    // Пользователь сам собирает маску
                    break;
            }

            SyncIsUsedFlags();
        }

        /// <summary>
        /// Добавляет заголовок из левого списка в маску.
        /// </summary>
        public void AddHeaderToMask(HeaderItem item)
        {
            if (item == null || item.IsUsed)
                return;

            string formatted = $"{{{item.Key}}}";

            if (MaskParts.Any(p => p.Text == formatted))
                return;

            MaskParts.Add(new MaskPart(formatted, isEditable: false));
            item.IsUsed = true;
        }

        /// <summary>
        /// Добавляет пользовательский текст в маску.
        /// </summary>
        public void AddCustomTextToMask()
        {
            if (string.IsNullOrWhiteSpace(CustomText))
                return;

            MaskParts.Add(new MaskPart(CustomText, isEditable: true));
            CustomText = string.Empty;
        }

        /// <summary>
        /// Удаляет часть маски.
        /// </summary>
        public void RemovePart(MaskPart part)
        {
            if (part == null)
                return;

            string key = part.Text.Trim('{', '}');
            HeaderItem header = AvailableHeaders.FirstOrDefault(h => h.Key == key);

            if (header != null)
                header.IsUsed = false;

            MaskParts.Remove(part);
        }

        /// <summary>
        /// Полностью очищает маску и снимает флаги IsUsed.
        /// </summary>
        public void ClearMaskParts()
        {
            foreach (HeaderItem header in AvailableHeaders)
                header.IsUsed = false;

            MaskParts.Clear();
        }

        /// <summary>
        /// Перемещает элемент маски вверх.
        /// </summary>
        public void MoveUp(int selectedIndex)
        {
            if (selectedIndex <= 0 || selectedIndex >= MaskParts.Count)
                return;

            MaskPart item = MaskParts[selectedIndex];
            MaskParts.RemoveAt(selectedIndex);
            MaskParts.Insert(selectedIndex - 1, item);
        }

        /// <summary>
        /// Перемещает элемент маски вниз.
        /// </summary>
        public void MoveDown(int selectedIndex)
        {
            if (selectedIndex < 0 || selectedIndex >= MaskParts.Count - 1)
                return;

            MaskPart item = MaskParts[selectedIndex];
            MaskParts.RemoveAt(selectedIndex);
            MaskParts.Insert(selectedIndex + 1, item);
        }

        #endregion

        #region Drag & Drop helpers (делегируются из MainWindow)

        /// <summary>
        /// Запоминаем стартовую точку для DnD.
        /// </summary>
        public void RememberDragStart(Point point)
        {
            _dragStartPoint = point;
        }

        /// <summary>
        /// Проверка - можно ли начинать Drag&Drop.
        /// </summary>
        public bool CanStartDrag(Point currentPoint)
        {
            return Math.Abs(currentPoint.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                   Math.Abs(currentPoint.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance;
        }

        /// <summary>
        /// Создаёт DragData для перетаскивания из AvailableHeaders в MaskParts.
        /// </summary>
        public Tuple<MaskPart, HeaderItem> CreateDragDataFromHeader(HeaderItem sourceItem)
        {
            if (sourceItem == null || sourceItem.IsUsed)
                return null;

            string formatted = $"{{{sourceItem.Key}}}";
            MaskPart part = new MaskPart(formatted, isEditable: false);

            return new Tuple<MaskPart, HeaderItem>(part, sourceItem);
        }

        /// <summary>
        /// Drop обратно в левый список (удаление из маски + снятие IsUsed).
        /// </summary>
        public void DropToAvailableList(MaskPart droppedPart)
        {
            if (droppedPart == null)
                return;

            string key = droppedPart.Text.Trim('{', '}');
            HeaderItem header = AvailableHeaders.FirstOrDefault(h => h.Key == key);

            if (header != null)
                header.IsUsed = false;

            MaskParts.Remove(droppedPart);
        }

        /// <summary>
        /// Drop в правый список (из левого или внутри правого).
        /// insertIndex = позиция вставки (если -1, добавляем в конец).
        /// </summary>
        public void DropToSelectedList(MaskPart dropped, HeaderItem sourceHeader, int insertIndex, bool isMoveInsideList)
        {
            if (dropped == null)
                return;

            if (isMoveInsideList)
                MaskParts.Remove(dropped);

            // Запрет дубликатов
            if (MaskParts.Any(p => p.Text == dropped.Text))
                return;

            if (insertIndex >= 0 && insertIndex <= MaskParts.Count)
                MaskParts.Insert(insertIndex, dropped);
            else
                MaskParts.Add(dropped);

            if (sourceHeader != null)
                sourceHeader.IsUsed = true;
        }

        #endregion

        #region Private

        /// <summary>
        /// Синхронизация флагов IsUsed на основе текущей маски.
        /// </summary>
        private void SyncIsUsedFlags()
        {
            foreach (HeaderItem header in AvailableHeaders)
                header.IsUsed = false;

            foreach (MaskPart part in MaskParts)
            {
                string key = part.Text.Trim('{', '}');
                HeaderItem header = AvailableHeaders.FirstOrDefault(h => h.Key == key);

                if (header != null)
                    header.IsUsed = true;
            }
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
