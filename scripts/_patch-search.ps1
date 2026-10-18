$ErrorActionPreference = 'Stop'
$p = 'C:\Users\zheng\Desktop\wiina\src\Wiina.Core\Settings\SettingsSearch.cs'
$s = [System.IO.File]::ReadAllText($p).Replace("`r`n", "`n")
function Rep([string]$a, [string]$b) {
    $a = $a.Replace("`r`n", "`n")
    $b = $b.Replace("`r`n", "`n")
    if (-not $script:s.Contains($a)) { throw "missing pattern: $a" }
    $script:s = $script:s.Replace($a, $b)
}

Rep "    public IReadOnlyList<SettingsSearchHit> Search(string? query, int limit = 20)
    {
        string text" "    public IReadOnlyList<SettingsSearchHit> Search(
        string? query,
        int limit = 20,
        string? language = null)
    {
        string resolvedLanguage = language ?? _language;
        string text"
Rep "int? score = ScoreRow(row, page, text, tokens);" "int? score = ScoreRow(row, page, text, tokens, resolvedLanguage);"
Rep "hits.Add(new SettingsSearchHit(page, row, score.Value * 1000 + index, _language));" "hits.Add(new SettingsSearchHit(page, row, score.Value * 1000 + index, resolvedLanguage));"
Rep "int? score = ScorePage(page, text, tokens);" "int? score = ScorePage(page, text, tokens, resolvedLanguage);"
Rep "hits.Add(new SettingsSearchHit(page, null, score.Value * 1000 + index, _language));" "hits.Add(new SettingsSearchHit(page, null, score.Value * 1000 + index, resolvedLanguage));"
Rep "        string query,
        string[] tokens)
    {
        string combined = string.Join(' '" "        string query,
        string[] tokens,
        string language)
    {
        string combined = string.Join(' '"
Rep "            SettingsLocalization.Title(row, _language)," "            SettingsLocalization.Title(row, language),"
Rep "            SettingsLocalization.Description(row, _language)," "            SettingsLocalization.Description(row, language),"
Rep "            SettingsLocalization.Section(row.Section, _language)," "            SettingsLocalization.Section(row.Section, language),"
Rep "            SettingsLocalization.PageTitle(page, _language)," "            SettingsLocalization.PageTitle(page, language),"
Rep "            SettingsLocalization.PageSummary(page, _language));" "            SettingsLocalization.PageSummary(page, language));"
Rep "int? full = BestFieldScore(row, page, query);" "int? full = BestFieldScore(row, page, query, language);"
Rep "int? tokenScore = BestFieldScore(row, page, token);" "int? tokenScore = BestFieldScore(row, page, token, language);"
Rep "        string query,
        string[] tokens)
    {
        string title = SettingsLocalization.PageTitle(page, _language);
        string summary = SettingsLocalization.PageSummary(page, _language);" "        string query,
        string[] tokens,
        string language)
    {
        string title = SettingsLocalization.PageTitle(page, language);
        string summary = SettingsLocalization.PageSummary(page, language);"
Rep "    private int? BestFieldScore(
        SettingsRowDefinition row,
        SettingsPageInfo page,
        string query)" "    private int? BestFieldScore(
        SettingsRowDefinition row,
        SettingsPageInfo page,
        string query,
        string language)"
[System.IO.File]::WriteAllText($p, $s, (New-Object System.Text.UTF8Encoding($false)))

$p2 = 'C:\Users\zheng\Desktop\wiina\src\Wiina.App\Views\SettingsWindow.xaml.cs'
$s = [System.IO.File]::ReadAllText($p2)
$s = $s.Replace("SettingsLocalization.UnavailableReason(row.UnavailableReason, Loc.CurrentLanguage)", "SettingsLocalization.UnavailableReason(row.UnavailableReason ?? string.Empty, Loc.CurrentLanguage)")
[System.IO.File]::WriteAllText($p2, $s, (New-Object System.Text.UTF8Encoding($false)))
Write-Output "patched search"

