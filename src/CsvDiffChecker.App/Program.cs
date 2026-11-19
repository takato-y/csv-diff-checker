using CsvDiffChecker.Core;
using CsvDiffChecker.Presentation;

namespace CsvDiffChecker.App;

internal static class Program
{
    /// <summary>
    /// 部品を組み立てて画面を起動する（ここが部品のつなぎ目。差し替えるときはここだけを変える）。
    /// 起動引数（任意）: --old 旧ファイル --new 新ファイル --key 列名[,列名]
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
            MessageBox.Show($"予期しないエラーが発生しました。\n\n{e.Exception.Message}",
                MainPresenter.Title, MessageBoxButtons.OK, MessageBoxIcon.Error);

        var loader = new CsvTableLoader();
        var engine = new CsvDiffEngine();
        var writer = new DiffCsvWriter();
        Application.Run(new MainForm(StartupOptions.Parse(args),
            view => new MainPresenter(view, loader, engine, writer)));
    }
}
