function Test-AppExcluded {
    param($App,[string[]]$Patterns)
    $reasons=[Collections.Generic.List[string]]::new()
    if([string]$App.SystemComponent -eq '1'){$reasons.Add('SystemComponent=1')};if($App.ParentKeyName){$reasons.Add('ParentKeyNameあり')};if([string]$App.ReleaseType -match '^(Update|Hotfix|Security Update)$'){$reasons.Add("ReleaseType=$($App.ReleaseType)")};if([string]$App.Name -match 'KB\d+'){$reasons.Add('KB 更新プログラム')}
    if(-not [string]$App.Name){$reasons.Add('名称なし')}
    foreach($p in $Patterns){if([string]$App.Name -match $p){$reasons.Add("名前パターン: $p");break}}
    if($App.Source -eq 'Appx' -and ($App.SignatureKind -eq 'System' -or $App.IsFramework -or $App.NonRemovable -or $App.IsResourcePackage)){$reasons.Add('システム/Framework/NonRemovable/Resource Appx')}
    [pscustomobject]@{Excluded=($reasons.Count -gt 0);Reason=($reasons -join '; ')}
}
function Get-AppClassification {
    param($App,[datetime]$Now,[int]$UnusedDays,[bool]$TrackingEnabled,[string[]]$Patterns)
    $excluded=Test-AppExcluded $App $Patterns;if($excluded.Excluded){return [pscustomobject]@{Status='対象外';ExcludedReason=$excluded.Reason}}
    if($App.LastRunTime){if(([datetime]$App.LastRunTime) -le $Now.AddDays(-$UnusedDays)){return [pscustomobject]@{Status='候補A';ExcludedReason=''}};return [pscustomobject]@{Status='判定不能';ExcludedReason='使用中（最終実行が閾値以内）'}}
    if($null -ne $App.RunCount -and [long]$App.RunCount -gt 0){return [pscustomobject]@{Status='判定不能';ExcludedReason='証拠あり・時刻なし（実行回数の記録のみ）'}}
    if(-not $TrackingEnabled){return [pscustomobject]@{Status='判定不能';ExcludedReason='アプリ起動追跡が無効または不明'}}
    if($App.InstallDate -and $App.InstallDate -match '^\d{8}$'){try{$date=[datetime]::ParseExact($App.InstallDate,'yyyyMMdd',$null);if($date -le $Now.AddDays(-$UnusedDays)){return [pscustomobject]@{Status='候補B';ExcludedReason=''}}}catch{}}
    [pscustomobject]@{Status='判定不能';ExcludedReason='実行記録または有効なインストール日なし'}
}
function ConvertTo-NormalizedPath {param([string]$Path);if(!$Path){return ''};[IO.Path]::GetFullPath($Path).TrimEnd('\')}
