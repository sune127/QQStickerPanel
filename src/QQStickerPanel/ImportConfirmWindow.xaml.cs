using System.IO;
using System.Windows;
using QQStickerPanel.Models;
using QQStickerPanel.ViewModels;

namespace QQStickerPanel;

public partial class ImportConfirmWindow : Window
{
    public ImportConfirmWindow(ImportConfirmationRequest request)
    {
        InitializeComponent();
        Request = request;
        Result = null;

        SummaryText.Text = $"准备导入 {request.SourceFiles.Count} 个表情包，预计重复 {request.DuplicateCount} 个。";
        FileListBox.ItemsSource = request.SourceFiles
            .Take(6)
            .Select(Path.GetFileName)
            .Concat(request.SourceFiles.Count > 6 ? [$"还有 {request.SourceFiles.Count - 6} 个文件..."] : [])
            .ToList();
        CategoryBox.ItemsSource = request.Categories;
        CategoryBox.SelectedItem = request.InitialCategory ?? request.Categories.FirstOrDefault();
        HintText.Text = request.DuplicateCount > 0
            ? "取消去重会把重复图片作为副本保存，适合需要保留不同命名的场景。"
            : "可以在导入时直接写入标签或收藏，之后也能在右键菜单里修改。";
    }

    public ImportConfirmationRequest Request { get; }

    public ImportConfirmationResult? Result { get; private set; }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        var newCategoryName = NewCategoryBox.Text.Trim();
        var selectedCategory = CategoryBox.SelectedItem as StickerCategory;
        if (selectedCategory is null && string.IsNullOrWhiteSpace(newCategoryName))
        {
            MessageBox.Show(this, "请选择保存分类或输入新分类。", "确认导入", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new ImportConfirmationResult(
            selectedCategory ?? Request.Categories.First(),
            newCategoryName,
            TagsBox.Text.Trim(),
            FavoriteBox.IsChecked == true,
            DeduplicateBox.IsChecked == true);
        DialogResult = true;
    }
}
