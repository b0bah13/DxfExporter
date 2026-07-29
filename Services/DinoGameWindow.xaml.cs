using DxfExporter;
using Dino.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Dino.Services
{
    /// <summary>
    /// Логика взаимодействия для DinoGameWindow.xaml
    /// </summary>
    public partial class DinoGameWindow : Window
    {
        private DinoGameHost? _dinoHost;
        private DinoGameController? _controller;
        private const string GameFolder = @"K:\Автоматизация процессов\DinoRunner";
        private const string SavedFolder = @"K:\Автоматизация процессов\Report";

        public static bool CanStart => Directory.Exists(GameFolder);

        public DinoGameWindow()
        {
            InitializeComponent();

            Loaded += DinoGameWindow_Loaded;
            Closing += DinoGameWindow_Closing;
        }

        private async void DinoGameWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var settings = new DinoGameSettings
            {
                GameFolder = GameFolder,
                SavedFolder = SavedFolder,
                UserName = CommonOperations.GetUserName()
            };

            _dinoHost = new DinoGameHost(webView, settings);

            bool ok = await _dinoHost.InitializeAsync();

            if (!ok)
            {
                // Пасхалка не готова — тихо закрываем окно
                Close();
                return;
            }

            _controller = _dinoHost.Controller;

            // Навигация после готовности host.
            _dinoHost.Start();
        }

        private async void DinoGameWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_dinoHost is null)
                return;

            // Отменяем закрытие, чтобы дождаться async-сохранения.
            // После завершения — отписываемся и закрываем повторно.
            e.Cancel = true;
            Closing -= DinoGameWindow_Closing;

            DinoScoreSaveResult? result;

            try
            {
                result = await _dinoHost.SaveSessionResultsAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Не удалось сохранить результаты игры:\n{ex.Message}");
                Close();
                return;
            }

            if (result is not null)// && result.Saved)
            {
                ShowResult(result);
            }

            // Теперь действительно закрываем окно (обработчик уже отписан).
            Close();
        }

        /// <summary>
        /// Показать результаты
        /// </summary>
        /// <param name="result"></param>
        private void ShowResult(DinoScoreSaveResult? result)
        {
            // После записи — проверка, что у пользователя больше всех очков в таблице.
            if (result.IsOverallLeader)
            {
                MessageBox.Show($"Лучший результат среди всех игроков!\n\n" +
                                $"Очки: {result.CurrentHighestScore}",
                    "Dino", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Личный рекорд: только если пользователь уже был в таблице и побил прошлый результат.
            if (result.BeatPersonalRecord)
            {
                MessageBox.Show($"Новый личный рекорд!\n\n" +
                                $"Было: {result.PreviousHighestScore}\n" +
                                $"Стало: {result.CurrentHighestScore}",
                    "Dino", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            //MessageBox.Show($"Результат забега: {result.PreviousHighestScore}",
                //"Dino", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}