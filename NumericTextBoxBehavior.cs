using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DxfExporter;

/// <summary>
/// Режимы валидации числового ввода для <see cref="TextBox"/>.
/// </summary>
public enum NumericInputMode
{
    /// <summary>
    /// Валидация отключена.
    /// </summary>
    None,

    /// <summary>
    /// Разрешены только целые числа (цифры).
    /// </summary>
    Integer,

    /// <summary>
    /// Разрешены десятичные числа с учётом текущего регионального разделителя.
    /// </summary>
    Decimal
}

/// <summary>
/// Attached behavior для ограничения ввода в <see cref="TextBox"/> только числовыми значениями.
/// </summary>
public static class NumericTextBoxBehavior
{
    /// <summary>
    /// Attached property, задающее режим числового ввода.
    /// </summary>
    public static readonly DependencyProperty InputModeProperty = DependencyProperty.RegisterAttached(
        "InputMode",
        typeof(NumericInputMode),
        typeof(NumericTextBoxBehavior),
        new PropertyMetadata(NumericInputMode.None, OnInputModeChanged));

    /// <summary>
    /// Возвращает режим ввода для указанного объекта.
    /// </summary>
    public static NumericInputMode GetInputMode(DependencyObject obj) => (NumericInputMode)obj.GetValue(InputModeProperty);

    /// <summary>
    /// Устанавливает режим ввода для указанного объекта.
    /// </summary>
    public static void SetInputMode(DependencyObject obj, NumericInputMode value) => obj.SetValue(InputModeProperty, value);

    /// <summary>
    /// Подключает или отключает обработчики ввода при изменении режима.
    /// </summary>
    private static void OnInputModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox textBox)
        {
            return;
        }

        textBox.PreviewTextInput -= TextBoxOnPreviewTextInput;
        DataObject.RemovePastingHandler(textBox, OnPaste);

        if ((NumericInputMode)e.NewValue == NumericInputMode.None)
        {
            return;
        }

        textBox.PreviewTextInput += TextBoxOnPreviewTextInput;
        DataObject.AddPastingHandler(textBox, OnPaste);
    }

    /// <summary>
    /// Проверяет вводимые символы до применения к тексту.
    /// </summary>
    private static void TextBoxOnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        var mode = GetInputMode(textBox);
        var proposedText = GetProposedText(textBox, e.Text);
        e.Handled = !IsValid(proposedText, mode);
    }

    /// <summary>
    /// Проверяет вставляемый текст из буфера обмена.
    /// </summary>
    private static void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        if (!e.SourceDataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        var pastedText = e.SourceDataObject.GetData(DataFormats.Text) as string ?? string.Empty;
        var mode = GetInputMode(textBox);
        var proposedText = GetProposedText(textBox, pastedText);

        if (!IsValid(proposedText, mode))
        {
            e.CancelCommand();
        }
    }

    /// <summary>
    /// Формирует строку, которая получится после вставки/ввода новых символов.
    /// </summary>
    private static string GetProposedText(TextBox textBox, string newText)
    {
        var currentText = textBox.Text ?? string.Empty;
        var selectionStart = textBox.SelectionStart;
        var selectionLength = textBox.SelectionLength;

        if (selectionLength > 0)
        {
            currentText = currentText.Remove(selectionStart, selectionLength);
        }

        return currentText.Insert(selectionStart, newText);
    }

    /// <summary>
    /// Валидирует строку по выбранному режиму.
    /// </summary>
    private static bool IsValid(string text, NumericInputMode mode)
    {
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        return mode switch
        {
            NumericInputMode.Integer => IsInteger(text),
            NumericInputMode.Decimal => IsDecimal(text),
            _ => true
        };
    }

    /// <summary>
    /// Проверяет, что строка состоит только из цифр.
    /// </summary>
    private static bool IsInteger(string text)
    {
        foreach (var ch in text)
        {
            if (!char.IsDigit(ch))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Проверяет, что строка является десятичным числом и содержит не более одного регионального разделителя.
    /// </summary>
    private static bool IsDecimal(string text)
    {
        var separator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        var hasSeparator = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i].ToString();

            if (char.IsDigit(text[i]))
            {
                continue;
            }

            if (ch == separator && !hasSeparator)
            {
                hasSeparator = true;
                continue;
            }

            return false;
        }

        return true;
    }
}
