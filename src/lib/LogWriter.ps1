function ConvertTo-CsvCell {
    param([object]$Value,[switch]$ProtectFormula)
    if($null -eq $Value){$s=''}elseif($Value -is [datetime]){$s=$Value.ToString('yyyy-MM-dd HH:mm:ss')}else{$s=[string]$Value}
    if($ProtectFormula -and $s -match '^[=+@-]'){$s="'"+$s};'"'+$s.Replace('"','""')+'"'
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

function New-InspectorOutputDirectory {param([string]$Path);[IO.Directory]::CreateDirectory($Path)|Out-Null}
