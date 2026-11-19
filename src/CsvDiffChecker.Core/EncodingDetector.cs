using System.Text;

namespace CsvDiffChecker.Core;

/// <summary>
/// CSV ファイルの文字コードを判定する。
/// 日本の業務で使われる CSV は UTF-8（BOM の有無）と Shift_JIS が大半のため、
/// BOM → UTF-8 として正しく読めるか → Shift_JIS の順に判定する。
/// </summary>
public static class EncodingDetector
{
    static EncodingDetector()
    {
        // Shift_JIS（コードページ 932）は既定では使えないため、プロバイダを登録する。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Encoding ShiftJis => Encoding.GetEncoding(932);

    /// <summary>判定結果。BomLength は本文の前に読み飛ばすバイト数。</summary>
    public sealed record Result(Encoding Encoding, string DisplayName, int BomLength);

    public static Result Detect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
            return new Result(new UTF8Encoding(false), "UTF-8（BOM付き）", 3);
        if (bytes.StartsWith(new byte[] { 0xFF, 0xFE }))
            return new Result(Encoding.Unicode, "UTF-16 LE", 2);
        if (bytes.StartsWith(new byte[] { 0xFE, 0xFF }))
            return new Result(Encoding.BigEndianUnicode, "UTF-16 BE", 2);

        // 不正なバイト列で例外を出す UTF-8 で読めるかを試す。
        // ASCII だけのファイルもここで UTF-8 と判定される（どちらで読んでも同じ内容になる）。
        var strictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
        try
        {
            strictUtf8.GetCharCount(bytes);
            return new Result(new UTF8Encoding(false), "UTF-8", 0);
        }
        catch (DecoderFallbackException)
        {
        }

        // UTF-8 として読めなければ Shift_JIS として読めるかを試す。
        // どちらでも読めないファイルを無理に読むと、文字化けした値が全行「変更」と出るため止める。
        var strictShiftJis = Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        try
        {
            strictShiftJis.GetCharCount(bytes);
            return new Result(ShiftJis, "Shift_JIS", 0);
        }
        catch (DecoderFallbackException)
        {
            throw new CsvFormatException(1,
                "文字コードを判定できません（UTF-8 としても Shift_JIS としても読めないバイトがあります）。ファイルが壊れていないか確認してください。");
        }
    }
}
