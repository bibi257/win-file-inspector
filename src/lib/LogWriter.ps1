function ConvertTo-CsvCell {
    param([object]$Value,[switch]$ProtectFormula)
    if($null -eq $Value){$s=''}elseif($Value -is [datetime]){$s=$Value.ToString('yyyy-MM-dd HH:mm:ss')}else{$s=[string]$Value}
    if($ProtectFormula -and $s -match '^(?:[=+@-]|\t|\r|\n)'){$s="'"+$s};'"'+$s.Replace('"','""')+'"'
}
function Write-InspectorCsvRow {
    param([IO.StreamWriter]$Writer,[string[]]$Columns,[object]$Row,[string[]]$ProtectColumns=@())
    $cells=foreach($c in $Columns){ConvertTo-CsvCell $Row.$c -ProtectFormula:($ProtectColumns -contains $c)};$Writer.WriteLine(($cells -join ','))
}
function Open-InspectorCsv {
    param([string]$OutputDir,[string]$Name,[string[]]$Columns)
    $path=Join-Path $OutputDir $Name;Assert-OutputPath $path $OutputDir
    $writer=[IO.StreamWriter]::new($path,$false,[Text.UTF8Encoding]::new($true));$writer.NewLine="`r`n";$writer.WriteLine((($Columns|ForEach-Object {ConvertTo-CsvCell $_}) -join ','));$writer
}
function Assert-OutputPath {param([string]$Path,[string]$OutputDir);$root=[IO.Path]::GetFullPath($OutputDir).TrimEnd('\')+[IO.Path]::DirectorySeparatorChar;$full=[IO.Path]::GetFullPath($Path);if(-not $full.StartsWith($root,[StringComparison]::OrdinalIgnoreCase)){throw '出力先フォルダ外への書き込みを拒否しました'} }
function Write-InspectorText {
    param([string]$OutputDir,[string]$Name,[string]$Content)
    $path=Join-Path $OutputDir $Name;Assert-OutputPath $path $OutputDir;$enc=[Text.UTF8Encoding]::new($true);[IO.File]::WriteAllText($path,$Content.Replace("`r`n","`n").Replace("`n","`r`n"),$enc)
}

function Assert-NoReparseOutputPath {param([string]$Path);$full=[IO.Path]::GetFullPath($Path);$current=$full;while($current){if([IO.Directory]::Exists($current) -or [IO.File]::Exists($current)){if(([IO.File]::GetAttributes($current) -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw "リパースポイントを含む出力先は使用できません: $current"}};$parent=[IO.Directory]::GetParent($current);if(!$parent){break};$current=$parent.FullName}}
function New-InspectorOutputDirectory {param([string]$Path);Assert-NoReparseOutputPath $Path;[IO.Directory]::CreateDirectory($Path)|Out-Null;Assert-NoReparseOutputPath $Path}
