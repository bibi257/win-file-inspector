function ConvertTo-InspectorPath {
    param([Parameter(Mandatory)][string]$Path)
    $full=[IO.Path]::GetFullPath($Path);$root=[IO.Path]::GetPathRoot($full)
    if([string]::Equals($full,$root,[StringComparison]::OrdinalIgnoreCase)){$full}else{$full.TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)}
}
function Test-PathWithin {
    param([string]$Path,[string]$Root)
    $p=([IO.Path]::GetFullPath($Path).Replace('\','/')).TrimEnd('/')+'/'; $r=([IO.Path]::GetFullPath($Root).Replace('\','/')).TrimEnd('/')+'/'
    $p.StartsWith($r,[StringComparison]::OrdinalIgnoreCase) -or [string]::Equals($p.TrimEnd('\'),$r.TrimEnd('\'),[StringComparison]::OrdinalIgnoreCase)
}
function Get-FileSystemType {
    param([string]$Path)
    try{$d=[IO.DriveInfo]::new([IO.Path]::GetPathRoot($Path));$d.DriveFormat}catch{$null}
}
function Invoke-FileScan {
    param([string[]]$Targets,[string[]]$Exclude,[string]$OutputDir,[int]$FolderDepth,[switch]$IncludeRawLastAccess,[switch]$IncludeRemovable,[object]$LastAccessSetting,[scriptblock]$OnFile,[scriptblock]$OnFolder,[scriptblock]$OnError,[scriptblock]$OnProgress)
    $count=0; $errorCount=0; $folderCounter=[pscustomobject]@{Count=0}; $sw=[Diagnostics.Stopwatch]::StartNew(); $lastProgress=[DateTime]::MinValue
    foreach($target in $Targets){
        try{$full=ConvertTo-InspectorPath $target;if($full.StartsWith('\\')){$OnError.Invoke($full,'ValidateTarget','NetworkPathRejected','UNC パスは走査しません');continue};$drive=[IO.DriveInfo]::new([IO.Path]::GetPathRoot($full));if($drive.DriveType -eq [IO.DriveType]::Network){$OnError.Invoke($full,'ValidateTarget','NetworkDriveRejected','ネットワークドライブは走査しません');continue};if($drive.DriveType -eq [IO.DriveType]::Removable -and -not $IncludeRemovable){$OnError.Invoke($full,'ValidateTarget','RemovableExcluded','-IncludeRemovable が指定されていません');continue}}catch{$OnError.Invoke($target,'ValidateTarget',$_.Exception.GetType().Name,$_.Exception.Message);continue}
        if([IO.Directory]::Exists($full) -and (([IO.File]::GetAttributes($full) -band [IO.FileAttributes]::ReparsePoint) -ne 0)){$OnError.Invoke($full,'ValidateTarget','ReparsePointRejected','対象がリパースポイントのため列挙しません');continue}
        $format=Get-FileSystemType $full
        $stack=[Collections.Generic.Stack[object]]::new();$stack.Push([pscustomobject]@{Path=$full;Depth=0;Exit=$false})
        $totals=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
        while($stack.Count){$frame=$stack.Pop();$path=$frame.Path
            if($Exclude | Where-Object {Test-PathWithin $path $_}){continue};if($OutputDir -and (Test-PathWithin $path $OutputDir)){continue}
            if($frame.Exit){$sum=$totals[$path];$parent=Split-Path -Parent $path;if($parent -and $totals.ContainsKey($parent)){$totals[$parent].Total += $sum.Total;$totals[$parent].Files += $sum.Files;if($sum.Errors){$totals[$parent].Errors=$true}};if($frame.Depth -le $FolderDepth){$OnFolder.Invoke([pscustomobject]@{Path=$path;Depth=$frame.Depth;DirectSizeBytes=$sum.Direct;TotalSizeBytes=$sum.Total;FileCount=$sum.Files;HadErrors=$sum.Errors});$folderCounter.Count++};continue}
            $direct=[long]0;$total=[long]0;$fileCount=0;$hadErrors=$false;$children=[Collections.Generic.List[object]]::new()
            try{$di=[IO.DirectoryInfo]::new($path);$items=$di.EnumerateFileSystemInfos();foreach($item in $items){try{$attr=$item.Attributes;if(($attr -band [IO.FileAttributes]::ReparsePoint) -ne 0){$OnFile.Invoke([pscustomobject]@{Path=$item.FullName;Type='ReparsePoint';SizeBytes='';CreationTime=$item.CreationTime;LastWriteTime=$item.LastWriteTime;LastAccessTime='';LastAccessTime_Raw=$item.LastAccessTime;LastAccessStatus='ReparsePoint';Attributes=[string]$attr;Extension=$item.Extension});$count++;continue};if($item -is [IO.DirectoryInfo]){$children.Add([pscustomobject]@{Path=$item.FullName;Depth=$frame.Depth+1})}else{$size=$item.Length;$direct+=$size;$total+=$size;$fileCount++;$OnFile.Invoke((New-InspectorFileRecord $item $size $attr $LastAccessSetting $format $IncludeRawLastAccess));$count++}}catch{$hadErrors=$true;$errorCount++;$OnError.Invoke($item.FullName,'EnumerateItem',$_.Exception.GetType().Name,$_.Exception.Message)}}}catch{$hadErrors=$true;$errorCount++;$OnError.Invoke($path,'EnumerateDirectory',$_.Exception.GetType().Name,$_.Exception.Message)}
            $total=$direct;$totals[$path]=[pscustomobject]@{Direct=$direct;Total=$total;Files=$fileCount;Errors=$hadErrors}
            $stack.Push([pscustomobject]@{Path=$path;Depth=$frame.Depth;Exit=$true})
            foreach($child in $children){$stack.Push([pscustomobject]@{Path=$child.Path;Depth=$child.Depth;Exit=$false})}
            if(($DateTimeNow=[DateTime]::UtcNow)-$lastProgress -ge [TimeSpan]::FromSeconds(1)){$OnProgress.Invoke($count,$path,$sw.Elapsed);$lastProgress=$DateTimeNow}
        }
    }
    $sw.Stop();[pscustomobject]@{FileCount=$count;FolderCount=$folderCounter.Count;ErrorCount=$errorCount;Elapsed=$sw.Elapsed}
}
function New-InspectorFileRecord {
    param($Item,[long]$Size,$Attributes,$LastAccessSetting,[string]$FileSystemType,[switch]$IncludeRawLastAccess)
    $format=$FileSystemType;$status='';$last=''
    if($format -ne 'NTFS'){$status=if($format){"非NTFS($format)"}else{'設定不明'}}elseif($LastAccessSetting -and $LastAccessSetting.Enabled){$last=$Item.LastAccessTime.ToString('yyyy-MM-dd HH:mm:ss')}else{$status=if($LastAccessSetting){$LastAccessSetting.Status}else{'設定不明'}}
    $r=[ordered]@{Path=$Item.FullName;Type='File';SizeBytes=$Size;CreationTime=$Item.CreationTime.ToString('yyyy-MM-dd HH:mm:ss');LastWriteTime=$Item.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss');LastAccessTime=$last;LastAccessStatus=$status;Attributes=[string]$Attributes;Extension=$Item.Extension}
    if($IncludeRawLastAccess){$r.LastAccessTime_Raw=$Item.LastAccessTime.ToString('yyyy-MM-dd HH:mm:ss')};[pscustomobject]$r
}
