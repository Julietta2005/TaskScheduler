using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace TaskScheduler
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private TaskManager _taskManager;
        public TaskManager TaskManager
        {
            get => _taskManager;
            set
            {
                if (_taskManager != value)
                {
                    _taskManager = value;
                    OnPropertyChanged(nameof(TaskManager));
                }
            }
        }
        private DispatcherTimer _deadlineCheckTimer;
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        public MainWindow()
        {
            InitializeComponent();
            TaskManager = new TaskManager();
            DataContext = this;
            // Настройка по умолчанию календаря
            TaskCalendar.SelectedDate = DateTime.Today;
            // Отображение задач для текущей даты
            FilterTasksByDate(DateTime.Today);
            // Настройка таймера
            _deadlineCheckTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(60)
            };
            _deadlineCheckTimer.Tick += (sender, e) => TaskManager.CheckDeadlines();
            _deadlineCheckTimer.Start();

            // Добавление обработчиков событий (если необходимо)
            // Подписка на события
            PriorityFilterComboBox.SelectionChanged += FilterComboBox_SelectionChanged;
            StatusFilterComboBox.SelectionChanged += FilterComboBox_SelectionChanged;
            TaskManager.OverdueTasksFound += ShowNotification;
        }
        private void ShowNotification(bool hasOverdue)
        {
            NotificationPopup.IsOpen = hasOverdue;

            Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        }
        private void TaskCalendar_SelectedDateChanged(object sender, SelectionChangedEventArgs e)     //добавление календаря
        {
            if (TaskCalendar.SelectedDate.HasValue)
                FilterTasksByDate(TaskCalendar.SelectedDate.Value);
        }
        private void FilterTasksByDate(DateTime selectedDate)
        {
            var tasksForSelectedDate = TaskManager.Tasks.Where(task => task.DueDate.Date == selectedDate.Date).ToList();
            TaskDataGrid.ItemsSource = tasksForSelectedDate;
        }
        private void AddTaskButton_Click(object sender, RoutedEventArgs e)                       //добавление задачи
        {
            var taskDialog = new TaskDialog();
            if (taskDialog.ShowDialog() == true)
            {
                // Проверяем, не просрочена ли дата
                if (taskDialog.Task.DueDate < DateTime.Now)
                    taskDialog.Task.Status = "Просрочено"; // Устанавливаем статус "Просрочено"
                else
                    taskDialog.Task.Status = "Planned"; // Если дата не просрочена, устанавливаем статус как "Planned"

                TaskManager.AddTask(taskDialog.Task);
                TaskManager.UpdateStatistics();
                FilterTasksByDate(TaskCalendar.SelectedDate.Value);
                // После добавления обновляем отображение задач с учетом фильтров
                FilterComboBox_SelectionChanged(null, null);  // Применяем фильтры
            }
        }
        private void DeleteTaskButton_Click(object sender, RoutedEventArgs e)
        {
            if (TaskDataGrid.SelectedItem is TaskItem selectedTask)
                TaskManager.RemoveTask(selectedTask);
        }
        private void MarkCompletedButton_Click(object sender, RoutedEventArgs e)
        {
            if (TaskDataGrid.SelectedItem is TaskItem selectedTask)
            {
                // Запрашиваем подтверждение для архивирования задачи
                var result = MessageBox.Show("Вы желаете, чтобы задача была архивирована?",
                    "Подтверждение",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    selectedTask.Status = "Completed";
                    TaskManager.ArchiveTask(selectedTask); 
                }
                else
                    selectedTask.Status = "Completed"; 
                

                TaskManager.UpdateStatistics(); 
                FilterTasksByDate(TaskCalendar.SelectedDate.Value); 
            }
        }
        private void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TaskManager == null) return;

            string selectedPriority = (PriorityFilterComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Все приоритеты";
            string selectedStatus = (StatusFilterComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Все статусы";

            if (selectedStatus == "Completed" || selectedStatus == "Archived")
                TaskDataGrid.ItemsSource = TaskManager.CompletedTasks;
            else
            {
                var filteredTasks = TaskManager.GetFilteredView(selectedPriority, selectedStatus);
                TaskDataGrid.ItemsSource = filteredTasks;
            }
        }
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new SaveFileDialog { Filter = "JSON Files|*.json" };
            if (saveFileDialog.ShowDialog() == true)
                TaskManager.SaveToFile(saveFileDialog.FileName);
            
        }
        private void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog { Filter = "JSON Files|*.json" };
            if (openFileDialog.ShowDialog() == true)
            {
                TaskManager.LoadFromFile(openFileDialog.FileName);

                FilterComboBox_SelectionChanged(null, null);

                TaskManager.UpdateStatistics();
                TaskDataGrid.ItemsSource = TaskManager.Tasks;  
            }
        }
        private void ShowAllTasksButton_Click(object sender, RoutedEventArgs e)
        {
            // Сбрасываем фильтры
            PriorityFilterComboBox.SelectedIndex = 0; // Выбираем "Все приоритеты"
            StatusFilterComboBox.SelectedIndex = 0;   // Выбираем "Все статусы"

            // Применяем фильтрацию (на самом деле убираем её)
            TaskDataGrid.ItemsSource = TaskManager.Tasks;
        }
        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)       //поиск
        {
            if (TaskManager == null) return;

            string query = SearchTextBox.Text;
            var searchResults = TaskManager.SearchTasks(query);

            TaskDataGrid.ItemsSource = searchResults;
        }
        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Text = string.Empty;
            TaskDataGrid.ItemsSource = TaskManager.Tasks;
        }
        private void ShowCompletedTasksButton_Click(object sender, RoutedEventArgs e) => TaskDataGrid.ItemsSource = TaskManager.CompletedTasks;
        private void TaskDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.Column.Header.ToString() != "Status" || e.Row.Item is not TaskItem task) return;
            if (task.Status == "Archived")
                TaskManager.ArchiveTask(task); // Архивация выполненной задачи
                
            TaskManager.UpdateStatistics();
        }
    }

}