using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Common;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace StaticBlogEditor;
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        Title = AppTitleTextBlock.Text;

        Status.statusBar = StatusBar;
        Status.dispatcherQueue = DispatcherQueue;

        // WebView2 EditorUI = new();
        // ZoomIn.KeyboardAcceleratorTextOverride = ZoomInText.Text;
        // ZoomOut.KeyboardAcceleratorTextOverride = ZoomOutText.Text;
    }
    void AutoSave_Toggled(object sender, RoutedEventArgs e)
    {
        try{
            EnableAutoSave.Visibility = AutoSave.IsOn ? Visibility.Visible : Visibility.Collapsed;
            DisableAutoSave.Visibility = AutoSave.IsOn ? Visibility.Collapsed : Visibility.Visible;
        }catch(Exception ex){
            Status.AddMessage(ex.Message);
        }
    }

    async void ClickOpen(object sender, RoutedEventArgs e)
    {
        await FilePicker.Open(this);
    }

    async void ClickSave(object sender, RoutedEventArgs e)
    {
        await FilePicker.Save(this, "Save");
    }

    async void ClickSaveAs(object sender, RoutedEventArgs e)
    {
        await FilePicker.Save(this, "Save as");
    }

    void ClickZoomIn(object sender, RoutedEventArgs e)
    {
        Status.AddMessage($"Zoom In");
    }

    void ClickZoomOut(object sender, RoutedEventArgs e)
    {
        Status.AddMessage($"Zoom out");
    }

    void ClickRestoreDefaultZoom(object sender, RoutedEventArgs e)
    {
        Status.AddMessage($"Restore default zoom");
    }

    async void ClickAbout(object sender, RoutedEventArgs e)
    {
        await Dialog.Show(Content, "This app is an example app for Windows App SDK!", "About");
        Status.AddMessage($"Thank you for using this app!");
    }

    void ClickExit(object sender, RoutedEventArgs e)
    {
        Close();
    }

    void Grid_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
    }

    async void Grid_Drop(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync();
            var files = items.OfType<StorageFile>().ToList();

            if (files.Count > 0)
            {
                // 例：最初のファイルのパスを WebView2 に渡す
                StorageFile file = files[0];
                string path = file.Path;

                string fileName = file.Name;
                IBuffer buffer = await FileIO.ReadBufferAsync(file);
                string base64Content = Convert.ToBase64String(WindowsRuntimeBufferExtensions.ToArray(buffer));
                // string content = await FileIO.ReadTextAsync(file);
                
                // string base64Content = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(content));

                var json = new { fileName, base64Content };
                string jsonString = System.Text.Json.JsonSerializer.Serialize(json);

                await EditorUI.CoreWebView2.ExecuteScriptAsync(
                    $"uploadFile('{jsonString}')"
                );
            }
        }
    }

}