using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Jaravi.Dashboard;

public partial class MainWindow : Window
{
    private INotifyCollectionChanged? _observedLogs;

    public MainWindow()
    {
        InitializeComponent();

        // Auto-scroll the log console: view-only concern, kept out of the ViewModels.
        // ItemsSource swaps whenever the selected session changes.
        DependencyPropertyDescriptor
            .FromProperty(ItemsControl.ItemsSourceProperty, typeof(ListBox))
            .AddValueChanged(LogList, (_, _) => HookLogCollection());
        LogList.Loaded += (_, _) => HookLogCollection();
    }

    private void HookLogCollection()
    {
        if (_observedLogs is not null)
            _observedLogs.CollectionChanged -= OnLogsChanged;

        _observedLogs = LogList.ItemsSource as INotifyCollectionChanged;
        if (_observedLogs is not null)
            _observedLogs.CollectionChanged += OnLogsChanged;
    }

    private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || LogList.Items.Count == 0) return;
        LogList.ScrollIntoView(LogList.Items[^1]);
    }
}
