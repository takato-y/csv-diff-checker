"""テスト仕様書（docs/04_テスト仕様書.md）のテストケース一覧を、テストコードから作り直す。

テストのクラス名とメソッド名から一覧を作るため、テストを足したり名前を変えたりしたら、このスクリプトを実行する。
一覧は <!-- tc-list:begin --> と <!-- tc-list:end --> の間だけを書き換える。

使い方（リポジトリの直下で）:
    python3 scripts/update_test_spec.py           # 書き換える
    python3 scripts/update_test_spec.py --check   # 書き換えずに、一覧が最新かだけを確かめる（最新でなければ終了コード 1）
"""
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
TEST_DIR = ROOT / "tests" / "CsvDiffChecker.Tests"
SPEC = ROOT / "docs" / "04_テスト仕様書.md"

# テストクラス → 対象の機能。並び順は一覧の並び順になる。
CLASS_TO_FUNCTION = {
    "CsvParserTests": "FN-001",
    "CsvTableTests": "FN-001",
    "CsvDiffEngineTests": "FN-002",
    "SampleFilesTests": "FN-002",
    "DiffCsvWriterTests": "FN-003",
    "MainPresenterTests": "FN-004",
    "StartupOptionsTests": "FN-005",
}

TEST_PATTERN = re.compile(
    r"\[(Fact|Theory)\]\s*\n((?:\s*\[InlineData[^\n]*\n)*)\s*public (?:async Task|void) (\w+)\(")


def build_table() -> tuple[str, int, int]:
    files = {p.stem: p for p in TEST_DIR.rglob("*Tests.cs")}
    unknown = sorted(set(files) - set(CLASS_TO_FUNCTION))
    if unknown:
        raise SystemExit(f"対象の機能が決まっていないテストクラスがあります: {', '.join(unknown)}（CLASS_TO_FUNCTION に追加してください）")

    rows, counters, total = [], {}, 0
    for cls, function in CLASS_TO_FUNCTION.items():
        text = files[cls].read_text(encoding="utf-8")
        for kind, inline, name in TEST_PATTERN.findall(text):
            counters[function] = counters.get(function, 0) + 1
            runs = len(re.findall(r"\[InlineData", inline)) if kind == "Theory" else 1
            total += runs
            case_id = f"TC-{function.replace('-', '')}-{counters[function]:03d}"
            rows.append(f"| {case_id} | {function} | {name.replace('_', '、')} | `{cls}.{name}` | {runs} |")

    # 結果の列は持たない。固定の「合格」を書くと、テストが失敗しても一覧が合格のままになるため。
    header = "| ID | 対象 | 確認すること | テスト（クラス.メソッド） | 件数 |\n|---|---|---|---|---|\n"
    summary = f"\n\n合計 {len(rows)} ケース、実行 {total} 件。"
    return header + "\n".join(rows) + summary, len(rows), total


def main() -> int:
    table, cases, runs = build_table()
    spec = SPEC.read_text(encoding="utf-8")
    # 目印の間が空でも、既に一覧があっても一致するようにする。
    pattern = re.compile(r"(<!-- tc-list:begin -->\n)(?:.*?\n)?(<!-- tc-list:end -->)", re.S)
    if not pattern.search(spec):
        raise SystemExit("テスト仕様書に tc-list の目印がありません。")
    updated = pattern.sub(lambda m: m.group(1) + table + "\n" + m.group(2), spec)

    if "--check" in sys.argv:
        if updated != spec:
            print("テスト仕様書の一覧が最新ではありません。python3 scripts/update_test_spec.py を実行してください。")
            return 1
        print(f"テスト仕様書の一覧は最新です（{cases} ケース、実行 {runs} 件）。")
        return 0

    SPEC.write_text(updated, encoding="utf-8")
    print(f"テスト仕様書の一覧を更新しました（{cases} ケース、実行 {runs} 件）。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
