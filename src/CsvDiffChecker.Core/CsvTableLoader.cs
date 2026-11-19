namespace CsvDiffChecker.Core;

/// <summary>ファイルを読み、文字コードを判定して <see cref="CsvTable"/> にする。</summary>
public sealed class CsvTableLoader : ICsvTableLoader
{
    /// <summary>
    /// 元のファイルは変更しない。
    /// Excel で開いたままのファイルも読めるよう、他のアプリの書き込みを許可して開く。
    /// </summary>
    public CsvTable Load(string path)
    {
        byte[] bytes;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                   FileShare.ReadWrite | FileShare.Delete))
        using (var memory = new MemoryStream())
        {
            stream.CopyTo(memory);
            bytes = memory.ToArray();
        }

        var detected = EncodingDetector.Detect(bytes);
        var text = detected.Encoding.GetString(bytes, detected.BomLength, bytes.Length - detected.BomLength);
        return CsvTable.FromText(text, detected.DisplayName);
    }
}
