using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AsicFarm.Client.Behaviors;

public static class TimeMaskBehavior
{
    public static readonly System.Windows.DependencyProperty IsEnabledProperty =
        System.Windows.DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(TimeMaskBehavior),
            new System.Windows.PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject d, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBox textBox)
        {
            if ((bool)e.NewValue)
            {
                textBox.PreviewTextInput += OnPreviewTextInput;
                textBox.TextChanged += OnTextChanged;
                DataObject.AddPastingHandler(textBox, OnPaste);
            }
            else
            {
                textBox.PreviewTextInput -= OnPreviewTextInput;
                textBox.TextChanged -= OnTextChanged;
                DataObject.RemovePastingHandler(textBox, OnPaste);
            }
        }
    }

    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // Разрешаем вводить только цифры
        e.Handled = !e.Text.All(char.IsDigit);
    }

    private static void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(typeof(string)))
        {
            var text = (string)e.DataObject.GetData(typeof(string))!;
            // Разрешаем вставку только если это цифры
            if (!text.All(char.IsDigit))
                e.CancelCommand();
        }
        else
        {
            e.CancelCommand();
        }
    }

    private static void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        var textBox = (TextBox)sender;

        // Предотвращаем рекурсию
        textBox.TextChanged -= OnTextChanged;

        var text = textBox.Text;
        var digits = new string(text.Where(char.IsDigit).ToArray());

        if (digits.Length > 4) digits = digits.Substring(0, 4);

        string formatted = digits;
        if (digits.Length >= 3)
        {
            formatted = digits.Insert(2, ":");
        }
        else if (digits.Length == 2 && text.EndsWith(":") == false && textBox.CaretIndex == 2)
        {
            formatted = digits + ":";
        }

        // Ограничение: часы не больше 23, минуты не больше 59
        if (formatted.Length >= 2)
        {
            if (int.TryParse(formatted.Substring(0, 2), out int hours) && hours > 23)
                formatted = "23" + (formatted.Length > 2 ? formatted.Substring(2) : "");
        }
        if (formatted.Length == 5)
        {
            if (int.TryParse(formatted.Substring(3, 2), out int minutes) && minutes > 59)
                formatted = formatted.Substring(0, 3) + "59";
        }

        if (textBox.Text != formatted)
        {
            textBox.Text = formatted;
            textBox.CaretIndex = formatted.Length;
        }

        textBox.TextChanged += OnTextChanged;
    }
}