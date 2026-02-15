using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DxfExporter;

public enum NumericInputMode
{
    None,
    Integer,
    Decimal
}

public static class NumericTextBoxBehavior
{
    public static readonly DependencyProperty InputModeProperty = DependencyProperty.RegisterAttached(
        "InputMode",
        typeof(NumericInputMode),
        typeof(NumericTextBoxBehavior),
        new PropertyMetadata(NumericInputMode.None, OnInputModeChanged));

    public static NumericInputMode GetInputMode(DependencyObject obj) => (NumericInputMode)obj.GetValue(InputModeProperty);

    public static void SetInputMode(DependencyObject obj, NumericInputMode value) => obj.SetValue(InputModeProperty, value);

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
